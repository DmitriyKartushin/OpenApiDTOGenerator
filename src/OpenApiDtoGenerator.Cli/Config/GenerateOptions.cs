using OpenApiDtoGenerator.Generators.Masking;

namespace OpenApiDtoGenerator.Cli.Config;

/// <summary>
/// Собранные параметры запуска CLI после парсинга опций.
/// Один источник истины для команды generate.
/// </summary>
public sealed record GenerateOptions
{
    /// <summary>Абсолютный путь к входному OpenAPI YAML/JSON файлу.</summary>
    public required string InputPath { get; init; }

    /// <summary>Абсолютный путь к директории вывода.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Основной namespace для DTO классов.</summary>
    public required string DtoNamespace { get; init; }

    /// <summary>Namespace для валидаторов.</summary>
    public string ValidatorsNamespace { get; init; } = "Validators";

    /// <summary>Namespace для masking-инфраструктуры.</summary>
    public string MaskingNamespace { get; init; } = "Masking";

    /// <summary>Опциональный префикс имён классов (например "Dto").</summary>
    public string ClassPrefix { get; init; } = string.Empty;

    /// <summary>Опциональный суффикс имён классов (например "Dto").</summary>
    public string ClassSuffix { get; init; } = string.Empty;

    /// <summary>Путь к YAML-конфигу маскировки (опционально).</summary>
    public string? MaskingConfigPath { get; init; }

    /// <summary>Загруженная конфигурация маскировки (null если не указана).</summary>
    public MaskingConfig? MaskingConfig { get; init; }

    /// <summary>Генерировать только DTO (без валидаторов/masking).</summary>
    public bool DtoOnly { get; init; }

    /// <summary>Генерировать только валидаторы.</summary>
    public bool ValidatorsOnly { get; init; }

    /// <summary>Генерировать JSON Schema для тел входящих POST-запросов.</summary>
    public bool GenerateJsonSchema { get; init; }

    /// <summary>HTTP-методы, для тел запросов которых генерируется JSON Schema (null — только POST).</summary>
    public IReadOnlyList<string>? JsonSchemaHttpMethods { get; init; }

    /// <summary>Дополнительно генерировать JSON Schema для тел ответов (*.Rs.&lt;код&gt;.schema.json).</summary>
    public bool JsonSchemaResponses { get; init; }

    /// <summary>Генерировать только JSON Schema.</summary>
    public bool JsonSchemaOnly { get; init; }

    /// <summary>Генерировать только masking.</summary>
    public bool MaskingOnly { get; init; }

    /// <summary>Подробный вывод логов.</summary>
    public bool Verbose { get; init; }

    // ── Вычисляемые флаги ──────────────────────────────────────────────

    public bool ShouldGenerateDto        => !ValidatorsOnly && !MaskingOnly && !JsonSchemaOnly;
    public bool ShouldGenerateValidators => !DtoOnly && !MaskingOnly && !JsonSchemaOnly;
    public bool ShouldGenerateMasking    => !DtoOnly && !ValidatorsOnly && !JsonSchemaOnly && MaskingConfig is not null;
    public bool ShouldGenerateJsonSchema => JsonSchemaOnly || (GenerateJsonSchema && !DtoOnly && !ValidatorsOnly && !MaskingOnly);

    /// <summary>Нужно ли делать DTO partial (нужно если masking включён).</summary>
    public bool NeedsPartialDto => ShouldGenerateMasking;
}
