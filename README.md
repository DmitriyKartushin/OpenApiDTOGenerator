# OpenAPI DTO Generator

Генератор C# DTO классов, FluentValidation валидаторов и системы JSON-маскировки чувствительных данных из OpenAPI 3.x спецификации.

## Возможности

- ✅ **DTO классы** — с `[JsonPropertyName]`, XML-doc, nullable-аннотациями
- ✅ **Enum'ы** — с `[EnumMember]` для сохранения оригинальных значений
- ✅ **Наследование** — через `allOf` с именованной базовой схемой
- ✅ **FluentValidation** — правила из `pattern`, `minLength`, `maxLength`, `format`, `minimum`/`maximum` и др.
- ✅ **Каскадная валидация** — `SetValidator` для вложенных объектов
- ✅ **Условная валидация** — `.When(...)` через `discriminator`
- ✅ **JSON-маскировка** — `ToMaskedJson()` на каждом DTO, стратегии: Full/Partial/FirstChar/YearOnly/Hash
- ✅ **Все виды `$ref`** — локальные, относительные файлы, абсолютные, HTTP/HTTPS
- ✅ **Циклические ссылки** — детектируются, генерируются lazy-refs
- ✅ **JSON Schema** — draft 2020-12 для тел POST-запросов, для валидации входящих запросов

## Установка

```bash
git clone <repo>
cd OpenApiDtoGenerator
dotnet build
```

## Использование

### Базовая генерация

```bash
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate \
  --input samples/openapi_example.yaml \
  --output ./Generated \
  --namespace MyApp.Dto
```

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

### С маскировкой

```bash
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate \
  --input samples/openapi_example.yaml \
  --output ./Generated \
  --namespace MyApp.Dto \
  --masking-config samples/masking-config.yaml
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

```bash
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate \
  --input samples/openapi_example.yaml \
  --output ./Generated \
  --namespace MyApp.Dto \
  --json-schema            # или --json-schema-only
```

Для каждой POST-операции с JSON `requestBody` генерируется самодостаточная
JSON Schema (draft 2020-12) — все `$ref` (локальные и внешние) собраны в `$defs`:
```
Generated/
└── JsonSchema/
    ├── createPerson.schema.json ← имя = operationId (или post_<path>)
    └── index.json               ← [{ method, path, operationId, contentType, required, schemaFile }]
```

Конвертация OpenAPI 3.0 → JSON Schema:
- `nullable: true` → `type: ["string", "null"]` (для `enum` добавляется `null`; без `type` — `anyOf` с `{type: null}`)
- `exclusiveMinimum: true` + `minimum: N` → `exclusiveMinimum: N` (аналогично для maximum)
- `example` → `examples: [...]`; `discriminator`, `xml`, `externalDocs`, `x-*` удаляются
- `readOnly`-свойства исключаются из `required` (клиент их не передаёт)

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

```bash
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate \
  -i samples/complex/loan_application.yaml -o ./Generated -n Loan.Dto \
  --json-schema-only --json-schema-methods post put
```

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

// Маскированная — безопасно для логов
var maskedJson = person.ToMaskedJson();
// {"lastName":"И*****","passport":{"series":"****","number":"12**56"}}

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

var schema = JsonSchema.FromFile("Generated/JsonSchema/createPerson.schema.json");
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

## Тесты

```bash
dotnet test

# ~380 tests, all green
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
│  • RefResolver (local/relative/absolute/http) │
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

## Лицензия

MIT
