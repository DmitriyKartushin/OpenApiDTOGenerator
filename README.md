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

## Тесты

```bash
dotnet test

# ~350 tests, all green
```

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
└────────────┬─────────────────────────────────┘
             ▼
┌──────────────────────────────────────────────┐
│  CLI                                            │
│  • System.CommandLine                          │
│  • GenerateCommandExecutor                     │
└────────────┬─────────────────────────────────┘
             ▼
        .cs файлы на диске
```

## Лицензия

MIT
