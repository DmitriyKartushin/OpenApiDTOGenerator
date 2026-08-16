namespace OpenApiDtoGenerator.Core.Models;

/// <summary>
/// Одно свойство объектной схемы.
/// </summary>
public sealed class PropertyNode
{
    /// <summary>Имя свойства как в YAML (camelCase).</summary>
    public required string Name { get; init; }

    /// <summary>Схема (тип) свойства.</summary>
    public required SchemaNode Schema { get; init; }

    /// <summary>Является ли свойство обязательным (из секции required родителя).</summary>
    public bool IsRequired { get; init; }

    /// <summary>Описание из поля description.</summary>
    public string? Description { get; init; }
}
