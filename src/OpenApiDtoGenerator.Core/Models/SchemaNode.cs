namespace OpenApiDtoGenerator.Core.Models;

/// <summary>
/// Внутреннее представление одной OpenAPI Schema — узел графа IR.
/// </summary>
public sealed record SchemaNode
{
    // ── Идентификация ─────────────────────────────────────────────────────────

    /// <summary>
    /// Имя схемы (ключ в components/schemas).
    /// Для анонимных/вложенных схем — синтезируется генератором.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Оригинальная строка $ref до резолвинга.
    /// null — если схема определена inline.
    /// </summary>
    public string? OriginalRef { get; set; }

    // ── Тип ──────────────────────────────────────────────────────────────────

    /// <summary>object | string | integer | number | boolean | array | null</summary>
    public string? Type { get; init; }

    // ── Структура объекта ────────────────────────────────────────────────────

    public IReadOnlyList<PropertyNode> Properties { get; init; } = [];

    // ── Полиморфизм ──────────────────────────────────────────────────────────

    /// <summary>allOf — наследование / пересечение схем.</summary>
    public IReadOnlyList<SchemaNode> AllOf { get; init; } = [];

    /// <summary>oneOf — ровно одна из схем.</summary>
    public IReadOnlyList<SchemaNode> OneOf { get; init; } = [];

    /// <summary>anyOf — одна или несколько схем.</summary>
    public IReadOnlyList<SchemaNode> AnyOf { get; init; } = [];

    /// <summary>Имя свойства-дискриминатора (из discriminator.propertyName).</summary>
    public string? DiscriminatorPropertyName { get; init; }

    /// <summary>Маппинг значений дискриминатора → имя схемы.</summary>
    public IReadOnlyDictionary<string, string> DiscriminatorMapping { get; init; }
        = new Dictionary<string, string>();

    // ── Массив ───────────────────────────────────────────────────────────────

    /// <summary>Схема элементов массива (заполняется если Type == "array").</summary>
    public SchemaNode? Items { get; init; }

    // ── Enum ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<string> EnumValues { get; init; } = [];

    // ── Валидация ─────────────────────────────────────────────────────────────

    public ValidationRules Rules { get; init; } = ValidationRules.Empty;

    // ── Мета ─────────────────────────────────────────────────────────────────

    public string? Description { get; init; }

    /// <summary>
    /// Флаг: схема является "lazy" заглушкой из-за циклической ссылки.
    /// Генератор разрешит её позже по Name.
    /// </summary>
    public bool IsLazyRef { get; set; }

    // ── Вычисляемые свойства ─────────────────────────────────────────────────

    public bool IsObject => Type == "object" || Properties.Count > 0;
    public bool IsArray  => Type == "array";
    public bool IsEnum   => EnumValues.Count > 0;
    public bool IsPrimitive => Type is "string" or "integer" or "number" or "boolean";
    public bool HasPolymorphism => AllOf.Count > 0 || OneOf.Count > 0 || AnyOf.Count > 0;
}
