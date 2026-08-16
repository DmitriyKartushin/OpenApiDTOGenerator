using OpenApiDtoGenerator.Core.Models;

namespace OpenApiDtoGenerator.Generators.Dto;

/// <summary>
/// Маппит OpenAPI типы (type + format) в строковые представления C# типов.
///
/// Правила:
///   string              → string
///   string + date        → DateOnly
///   string + date-time   → DateTime
///   string + uuid        → Guid
///   string + byte        → byte[]
///   integer              → int
///   integer + int32      → int
///   integer + int64      → long
///   number                → double
///   number + float       → float
///   number + double      → double
///   boolean               → bool
///   array                 → List&lt;T&gt;
///   object (named)        → имя класса (SchemaNode.Name)
///   object (anon)         → object
///   enum (named)          → имя enum (SchemaNode.Name)
/// </summary>
public sealed class TypeMapper
{
    /// <summary>
    /// Возвращает строковое представление C# типа для данного узла схемы.
    /// </summary>
    /// <param name="node">Узел IR-схемы.</param>
    /// <param name="applyNullable">
    ///   Если true и node.Rules.Nullable == true — добавляет "?" к типу.
    /// </param>
    public string Map(SchemaNode node, bool applyNullable = false)
    {
        ArgumentNullException.ThrowIfNull(node);

        var baseType = MapCore(node);

        if (applyNullable && node.Rules.Nullable && !baseType.EndsWith('?') && !baseType.StartsWith("List<"))
            return baseType + "?";

        return baseType;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core mapping logic
    // ─────────────────────────────────────────────────────────────────────────

    private string MapCore(SchemaNode node)
    {
        // ── Enum: именованный тип, генерируется как C# enum ────────────────
        if (node.IsEnum && !string.IsNullOrEmpty(node.Name))
            return node.Name;

        return node.Type switch
        {
            "string"  => MapString(node),
            "integer" => MapInteger(node),
            "number"  => MapNumber(node),
            "boolean" => "bool",
            "array"   => MapArray(node),
            "object"  => MapObject(node),
            _         => MapDefault(node)
        };
    }

    private static string MapString(SchemaNode node) => node.Rules.Format switch
    {
        "date"      => "DateOnly",
        "date-time" => "DateTime",
        "uuid"      => "Guid",
        "byte"      => "byte[]",
        _           => "string"
    };

    private static string MapInteger(SchemaNode node) => node.Rules.Format switch
    {
        "int64" => "long",
        _       => "int" // включая "int32" и отсутствие формата
    };

    private static string MapNumber(SchemaNode node) => node.Rules.Format switch
    {
        "float" => "float",
        _       => "double" // включая "double" и отсутствие формата
    };

    private string MapArray(SchemaNode node)
    {
        var itemType = node.Items is not null
            ? MapCore(node.Items)
            : "object";

        return $"List<{itemType}>";
    }

    private static string MapObject(SchemaNode node)
        => !string.IsNullOrEmpty(node.Name) ? node.Name : "object";

    private static string MapDefault(SchemaNode node)
        => !string.IsNullOrEmpty(node.Name) ? node.Name : "object";
}
