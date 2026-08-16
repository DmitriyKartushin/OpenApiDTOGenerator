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

    /// <summary>Генерировать только masking.</summary>
    public bool MaskingOnly { get; init; }

    /// <summary>Подробный вывод логов.</summary>
    public bool Verbose { get; init; }

    // ── Вычисляемые флаги ──────────────────────────────────────────────

    public bool ShouldGenerateDto        => !ValidatorsOnly && !MaskingOnly;
    public bool ShouldGenerateValidators => !DtoOnly && !MaskingOnly;
    public bool ShouldGenerateMasking    => !DtoOnly && !ValidatorsOnly && MaskingConfig is not null;

    /// <summary>Нужно ли делать DTO partial (нужно если masking включён).</summary>
    public bool NeedsPartialDto => ShouldGenerateMasking;
}
