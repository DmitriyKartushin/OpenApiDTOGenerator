# OpenAPI DTO Generator

Генератор C# DTO классов, FluentValidation валидаторов и системы JSON-маскировки чувствительных данных из OpenAPI 3.x спецификации.

## Возможности

- ✅ **DTO классы** — с `[JsonPropertyName]`, XML-doc, nullable-аннотациями
- ✅ **Enum'ы** — с `[EnumMember]` для сохранения оригинальных значений
- ✅ **Наследование** — через `allOf` с именованной базовой схемой
- ✅ **FluentValidation** — правила из `pattern`, `minLength`, `maxLength`, `format`, `minimum`/`maximum` и др.
- ✅ **Каскадная валидация** — `SetValidator` для вложенных объектов
- ✅ **Условная валидация** — `.When(...)` через `discriminator`
- ⚠️ **JSON-маскировка** — `[Mask]` и `ToMaskedJson()` на каждом DTO генерируются, стратегии: Full/Partial/FirstChar/YearOnly/Hash. Но сейчас код не собирается и не маскирует (см. «Известные проблемы»)
- ✅ **`$ref`** — локальные, относительные и абсолютные файлы. HTTP/HTTPS распознаются, но загрузчика пока нет: такие ссылки приводят к ошибке
- ✅ **Циклические ссылки** — детектируются, генерируются lazy-refs
- ✅ **JSON Schema** — draft 2020-12 для тел POST-запросов (`*.Rq.schema.json`) и, опционально, ответов (`*.Rs.<код>.schema.json`)

## Установка

```bash
git clone <repo>
cd OpenApiDtoGenerator
dotnet build
```

## Использование

### Базовая генерация

```bat
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate --input samples/openapi_example.yaml --output Generated --namespace MyApp.Dto
```

Команды написаны в одну строку: многострочный ввод через `\` не работает в cmd и PowerShell (в PowerShell — `` ` ``, в cmd — `^`), поэтому лучше не переносить строки.

Результат:
```
Generated/
├── Dto/
│   ├── PersonInfo.cs
│   ├── PassportInfo.cs
│   ├── INN.cs
│   ├── SNILS.cs
│   └── DocumentType.cs         ← enum
└── Validators/
    ├── PersonInfoValidator.cs
    ├── PassportInfoValidator.cs
    └── ...
```

### Простой пример: `samples/open_api_example_simple.yaml`

Минимальная спецификация: один `POST /send` с телом `SendRequest` (в нём массив `Item`) и ответами `200` (`SendResponse`) и `400` (без `content`, файла не даёт).

```bat
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate -i samples/open_api_example_simple.yaml -o Generated -n Simple.Dto --json-schema --json-schema-responses
```

Результат:
```
Generated/
├── Dto/          SendRequest.cs, Item.cs, SendResponse.cs
├── Validators/   SendRequestValidator.cs, ItemValidator.cs, SendResponseValidator.cs
└── JsonSchema/   sendPayload.Rq.schema.json, sendPayload.Rs.200.schema.json, index.json
```

На этом примере хорошо видно, где генератор пока неточен (подробнее — `presentation/speaker-notes.md`, раздел 7):
- `items: { type: array, items: $ref Item }` → `List<object>`: массивы-свойства не разворачиваются в IR.
- `priority: integer, enum: [1, 2, 3]` → `int` без проверки enum: целочисленные enum пропускаются.
- `status: enum: [SENT, QUEUED]` (inline) → `string` с `IsInEnum()`, который на строке отклоняет любое значение.
- `id: format: uuid` → `Guid`, но валидатор вызывает `Guid.TryParse(v)`; то же для `sentAt` и `messageId`. Сгенерированные валидаторы этого примера не собираются (CS1503).
- JSON Schema при этом корректна: тело запроса валидируется полностью, включая `$ref` на `Item` через `$defs`.

### С маскировкой

```bat
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate --input samples/complex/loan_application.yaml --output Generated --namespace MyApp.Dto --masking-config samples/masking-config.yaml
```

Дополнительно генерируется:
```
Generated/
└── Masking/
    ├── MaskStrategy.cs          ← enum стратегий
    ├── MaskAttribute.cs         ← [Mask(...)]
    ├── MaskingStrategyExecutor.cs
    ├── MaskingJsonConverter.cs
    ├── MaskingJsonOptions.cs
    ├── PassportInfo.Masking.cs  ← partial class с ToMaskedJson()
    └── ...
```

### JSON Schema для валидации входящих запросов

```bat
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate --input samples/openapi_example.yaml --output Generated --namespace MyApp.Dto --json-schema
```

Вместо `--json-schema` можно указать `--json-schema-only`, чтобы сгенерировать только JSON Schema.

Для каждой POST-операции с JSON `requestBody` генерируется самодостаточная
JSON Schema (draft 2020-12) — все `$ref` (локальные и внешние) собраны в `$defs`.
Суффикс в имени файла показывает направление: `Rq` — тело запроса, `Rs` — тело ответа.
```
Generated/
└── JsonSchema/
    ├── createPerson.Rq.schema.json       ← запрос; имя = operationId (или post_<path>)
    ├── createPerson.Rs.201.schema.json   ← ответ с кодом 201 (только с --json-schema-responses)
    └── index.json                        ← [{ method, path, operationId, kind, statusCode, contentType, required, schemaFile }]
```

Схемы ответов включаются флагом `--json-schema-responses` (вместе с `--json-schema` или `--json-schema-only`):
для каждого JSON-ответа создаётся `<operationId>.Rs.<код>.schema.json` (код `default` → `Rs.default`).
Ответы без `content` (например, `204`) файлов не дают. Для ответов `readOnly`-свойства остаются в `required`.

Конвертация OpenAPI 3.0 → JSON Schema:
- `nullable: true` → `type: ["string", "null"]` (для `enum` добавляется `null`; без `type` — `anyOf` с `{type: null}`)
- `exclusiveMinimum: true` + `minimum: N` → `exclusiveMinimum: N` (аналогично для maximum)
- `example` → `examples: [...]`; `discriminator`, `xml`, `externalDocs`, `x-*` удаляются
- `readOnly`-свойства исключаются из `required` в схемах запросов (клиент их не передаёт)

Для других методов: `--json-schema-methods post put patch`.

Ограничения, которые важно знать при валидации:
- **`format` по умолчанию не проверяется.** В draft 2020-12 `format` (`date`, `email`, `uuid`…) — аннотация.
  Чтобы `"birthDate": "не-дата"` отклонялся, включите проверку форматов в валидаторе
  (для JsonSchema.Net — `RequireFormatValidation = true`, см. пример ниже).
- **`discriminator` не переносится** — `oneOf` выбирает вариант по содержимому. Чтобы варианты
  взаимно исключали друг друга, задавайте в каждом поле-дискриминатор с `enum: [VALUE]`
  (как в `samples/complex/loan_application.yaml`). При ошибке валидатор сообщит о несовпадении со всеми вариантами.

#### Сложный пример

`samples/complex/` — заявка на кредит: внешние `$ref` (`common/money.yaml`, `common/address.yaml`),
`allOf`, `oneOf` + `discriminator`, цикл `Comment → replies → Comment`, `nullable`, `readOnly`,
`exclusiveMinimum`, `additionalProperties`, `uniqueItems`. В `payloads/` лежат тела запросов:
`valid_*.json` должны проходить валидацию, `invalid_*.json` — отклоняться (нарушение указано в имени файла).
Их автоматически прогоняет `tests/Integration.Tests/ComplexRequestJsonSchemaTests.cs` —
чтобы проверить новый случай, достаточно добавить файл.

```bat
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate -i samples/complex/loan_application.yaml -o Generated -n Loan.Dto --json-schema-only --json-schema-methods post put
```

Одна команда принимает один `--input`: два входных файла за раз не поддерживаются.

### Все опции

```
generate
  --input, -i <FILE>              Путь к OpenAPI YAML/JSON (обязательно)
  --output, -o <DIR>              Директория вывода (по умолчанию: ./Generated)
  --namespace, -n <NS>            Namespace для DTO (обязательно)
  --validators-namespace <NS>     Namespace валидаторов (по умолчанию: <namespace>.Validators)
  --masking-namespace <NS>        Namespace masking (по умолчанию: <namespace>.Masking)
  --class-prefix <TEXT>           Префикс имён классов (например "Dto")
  --class-suffix <TEXT>           Суффикс имён классов (например "Dto")
  --masking-config <FILE>         Путь к YAML-конфигу маскировки
  --json-schema                   Дополнительно генерировать JSON Schema тел запросов
  --json-schema-methods <M...>    HTTP-методы для JSON Schema (по умолчанию: post)
  --json-schema-responses         Также JSON Schema тел ответов (с --json-schema или --json-schema-only)
  --dto-only                      Генерировать только DTO
  --validators-only               Генерировать только валидаторы
  --masking-only                  Генерировать только masking (требует --masking-config)
  --json-schema-only              Генерировать только JSON Schema
  --verbose, -v                   Подробный вывод
```

## Пример использования в коде

```csharp
using MyApp.Dto;

// Обычная сериализация — светит всё
var json = JsonSerializer.Serialize(person);

// Маскированная — задумано для логов, но сейчас НЕ маскирует (см. «Известные проблемы»)
var maskedJson = person.ToMaskedJson();

// Валидация
var validator = new PersonInfoValidator();
var result = validator.Validate(person);
if (!result.IsValid)
{
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.PropertyName}: {error.ErrorMessage}");
}
```

### Валидация входящего запроса по JSON Schema

Пример для ASP.NET Core с [JsonSchema.Net](https://www.nuget.org/packages/JsonSchema.Net) —
схема загружается один раз и проверяет сырое тело запроса до десериализации в DTO:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

var schema = JsonSchema.FromFile("Generated/JsonSchema/createPerson.Rq.schema.json");
var options = new EvaluationOptions
{
    OutputFormat = OutputFormat.List,
    RequireFormatValidation = true   // проверять date / date-time / email / uuid
};

app.MapPost("/persons", async (HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var result = schema.Evaluate(body, options);

    if (!result.IsValid)
    {
        var errors = result.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => d.Errors!.Select(e => new { field = d.InstanceLocation.ToString(), error = e.Value }));
        return Results.BadRequest(errors);   // [{ "field": "/personInfo/lastName", "error": "..." }]
    }

    var dto = body.Deserialize<CreatePersonRequestBody>();
    // ...
    return Results.Created();
});
```

Для нескольких операций используйте `index.json`: загрузите все схемы и выбирайте нужную по `method` + `path`.

## Известные проблемы

Проверено сборкой сгенерированного кода (подробности и воспроизведение — `presentation/speaker-notes.md`, раздел 7):
- **Маскировка не работает.** `MaskingJsonConverter` не компилируется (CS0120 в `GetType()` внутри статического метода), а `MaskingJsonOptions.Default` не подключает конвертер. Даже после правки `ToMaskedJson()` возвращает JSON без маскировки.
- **Сгенерированные валидаторы часто не собираются.** `Guid`/`DateTime`, переданные в `TryParse(string)` (CS1503), переносы строк в `WithMessage` (CS1010), ссылки на несгенерированные классы (CS0246) для внешних `$ref`, `allOf` и `oneOf`.
- **HTTP(S) `$ref` не загружаются.** Ссылка распознаётся, но загрузчика нет.
- **Вложенные inline-структуры теряются.** Массивы и объекты внутри свойств становятся `List<object>` / `object`; целочисленные `enum` и inline-`enum` обрабатываются неверно.

## Тесты

```bash
dotnet test

# 386 тестов, все зелёные (~10 секунд)
```

| Проект | Что проверяет |
|---|---|
| `Core.Tests` | парсинг YAML/JSON, `$ref`-резолвинг, JSON Pointer, циклы, IRBuilder |
| `Generators.Tests` | DTO, валидаторы, masking, JSON Schema (включая реальную валидацию через JsonSchema.Net) |
| `Cli.Tests` | пайплайн `generate` с записью файлов на диск, флаги `--*-only` |
| `Integration.Tests` | `openapi_example.yaml` → IR; `samples/complex` → JSON Schema → валидация тел запросов |

## Архитектура

```
┌──────────────────────────────────────────────┐
│  OpenAPI YAML/JSON                            │
└────────────┬─────────────────────────────────┘
             ▼
┌──────────────────────────────────────────────┐
│  Core                                          │
│  • YamlDocumentParser                          │
│  • RefResolver (local/relative/absolute; http — нет загрузчика) │
│  • IRBuilder → SchemaNode граф                │
└────────────┬─────────────────────────────────┘
             ▼
┌──────────────────────────────────────────────┐
│  Generators                                    │
│  • DtoGenerator                                │
│  • ValidatorGenerator                          │
│  • MaskingGenerator                            │
│  • JsonSchemaGenerator (по сырому дереву)      │
└────────────┬─────────────────────────────────┘
             ▼
┌──────────────────────────────────────────────┐
│  CLI                                            │
│  • System.CommandLine                          │
│  • GenerateCommandExecutor                     │
└────────────┬─────────────────────────────────┘
             ▼
   .cs и .schema.json файлы на диске
```

## Презентация и заметки

- `presentation/index.html` — 27 слайдов: как устроен парсинг YAML, генерация DTO/валидаторов, JSON Schema, маскировка, кейсы и известные ограничения (`→`/`←` — навигация, `F` — полный экран, `P` — PDF).
- `presentation/speaker-notes.md` — заметки к слайдам, FAQ и воспроизводимые команды.

## Лицензия

MIT
