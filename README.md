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
  --dto-only                      Генерировать только DTO
  --validators-only               Генерировать только валидаторы
  --masking-only                  Генерировать только masking (требует --masking-config)
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
