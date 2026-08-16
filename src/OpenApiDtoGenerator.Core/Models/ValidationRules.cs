namespace OpenApiDtoGenerator.Core.Models;

/// <summary>
/// Правила валидации, извлечённые из OpenAPI схемы.
/// Все поля nullable — отсутствие значения означает «правило не задано».
/// </summary>
public sealed class ValidationRules
{
    // ── Строковые ────────────────────────────────────────────────────────────
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string? Pattern { get; init; }

    // ── Числовые ─────────────────────────────────────────────────────────────
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public bool ExclusiveMinimum { get; init; }
    public bool ExclusiveMaximum { get; init; }

    // ── Массивы ──────────────────────────────────────────────────────────────
    public int? MinItems { get; init; }
    public int? MaxItems { get; init; }
    public bool UniqueItems { get; init; }

    // ── Формат ───────────────────────────────────────────────────────────────
    /// <summary>date | date-time | uuid | email | byte | password | …</summary>
    public string? Format { get; init; }

    // ── Обязательность и nullable ─────────────────────────────────────────────
    public bool Required { get; init; }
    public bool Nullable { get; init; }

    public static readonly ValidationRules Empty = new();
}
