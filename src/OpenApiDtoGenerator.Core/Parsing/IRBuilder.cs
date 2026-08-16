using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Core.Resolving;

namespace OpenApiDtoGenerator.Core.Parsing;

/// <summary>
/// Разбирает распарсенный документ в граф SchemaNode'ов (IR).
///
/// Обрабатывает:
///   - components/schemas → отдельные SchemaNode'ы
///   - components/requestBodies → тоже SchemaNode'ы
///   - Полиморфизм: allOf, oneOf, anyOf, discriminator
///   - Валидационные правила
///   - Рекурсивные $ref'ы (через CycleDetector)
/// </summary>
public sealed class IRBuilder
{
    private readonly IRefResolver _refResolver;
    private readonly CycleDetector _cycleDetector = new();

    // Кеш построенных узлов — чтобы не строить дважды одну схему
    private readonly Dictionary<string, SchemaNode> _builtNodes = new();

    public IRBuilder(IRefResolver refResolver)
    {
        _refResolver = refResolver ?? throw new ArgumentNullException(nameof(refResolver));
    }

    /// <summary>
    /// Строит граф SchemaNode'ов из распарсенного документа.
    /// Результат — маппирование имён схем → SchemaNode'ы.
    /// </summary>
    /// <param name="documentTree">Распарсенное дерево (Dictionary корень документа).</param>
    /// <param name="documentKey">Ключ документа (для относительных $ref).</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task<Dictionary<string, SchemaNode>> BuildAsync(
        object? documentTree,
        string documentKey,
        CancellationToken ct = default)
    {
        if (documentTree is not Dictionary<string, object?> doc)
            throw new ArgumentException("Document tree must be a Dictionary.", nameof(documentTree));

        _builtNodes.Clear();
        _cycleDetector.Reset();

        // ── 1. Извлекаем schemas из components/schemas ──────────────────────
        var schemasDict = ExtractSchemas(doc);

        // ── 2. Строим каждую схему ──────────────────────────────────────────
        foreach (var (schemaName, schemaObj) in schemasDict)
        {
            if (schemaObj is not Dictionary<string, object?> schema)
                continue;

            var node = await BuildSchemaNodeAsync(
                schemaName,
                schema,
                documentKey,
                ct);

            _builtNodes[schemaName] = node;
        }

        return _builtNodes;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core building
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<SchemaNode> BuildSchemaNodeAsync(
        string schemaName,
        Dictionary<string, object?> schemaDict,
        string documentKey,
        CancellationToken ct)
    {
        // Быстрый путь — уже построили
        if (_builtNodes.TryGetValue(schemaName, out var cached))
            return cached;

        // Входим в цикл-детектор
        using (_cycleDetector.Scope(schemaName))
        {
            var node = new SchemaNode
            {
                Name = schemaName,
                Type = ExtractType(schemaDict),
                Description = ExtractDescription(schemaDict),
                Rules = ExtractValidationRules(schemaDict),
            };

            // ── Простые типы (примитивы и enum) ─────────────────────────────
            if (ExtractEnum(schemaDict) is List<string> enumValues && enumValues.Count > 0)
            {
                node = node with { EnumValues = enumValues };
                return node;
            }

            // ── Массив ──────────────────────────────────────────────────────
            if (node.Type == "array")
            {
                if (schemaDict.TryGetValue("items", out var itemsObj) && itemsObj is Dictionary<string, object?> items)
                {
                    var itemsNode = await BuildSchemaNodeFromInlineAsync(items, documentKey, ct);
                    node = node with { Items = itemsNode };
                }
            }

            // ── Объект: свойства ─────────────────────────────────────────────
            if (node.Type == "object" || schemaDict.ContainsKey("properties"))
            {
                var properties = await ExtractPropertiesAsync(schemaDict, documentKey, ct);
                var required = ExtractRequired(schemaDict);

                var propList = properties.Select(p => new PropertyNode
                {
                    Name = p.Key,
                    Schema = p.Value,
                    IsRequired = required.Contains(p.Key),
                    Description = ExtractPropertyDescription(schemaDict, p.Key)
                }).ToList();

                node = node with { Properties = propList };
            }

            // ── Полиморфизм: allOf, oneOf, anyOf ────────────────────────────
            if (schemaDict.TryGetValue("allOf", out var allOfObj) && allOfObj is List<object?> allOfList)
            {
                var allOfNodes = await BuildSchemaListAsync(allOfList, documentKey, ct);
                node = node with { AllOf = allOfNodes };
            }

            if (schemaDict.TryGetValue("oneOf", out var oneOfObj) && oneOfObj is List<object?> oneOfList)
            {
                var oneOfNodes = await BuildSchemaListAsync(oneOfList, documentKey, ct);
                var (discriminatorProp, discriminatorMap) = ExtractDiscriminator(schemaDict, oneOfList);
                node = node with
                {
                    OneOf = oneOfNodes,
                    DiscriminatorPropertyName = discriminatorProp,
                    DiscriminatorMapping = discriminatorMap
                };
            }

            if (schemaDict.TryGetValue("anyOf", out var anyOfObj) && anyOfObj is List<object?> anyOfList)
            {
                var anyOfNodes = await BuildSchemaListAsync(anyOfList, documentKey, ct);
                node = node with { AnyOf = anyOfNodes };
            }

            _builtNodes[schemaName] = node;
            return node;
        }
    }

    /// <summary>
    /// Строит SchemaNode из inline-схемы (без имени).
    /// Обрабатывает $ref, если есть.
    /// </summary>
    private async Task<SchemaNode> BuildSchemaNodeFromInlineAsync(
        Dictionary<string, object?> schemaDict,
        string documentKey,
        CancellationToken ct)
    {
        // Если есть $ref — резолвим его через RefResolver
        if (schemaDict.TryGetValue("$ref", out var refObj) && refObj is string refString)
        {
            try
            {
                var resolved = await _refResolver.ResolveAsync(refString, documentKey, ct);
                return resolved;
            }
            catch (CyclicReferenceException)
            {
                // На циклах — возвращаем lazy-ссылку
                return new SchemaNode
                {
                    OriginalRef = refString,
                    IsLazyRef = true
                };
            }
        }

        // Без $ref — разбираем inline
        return new SchemaNode
        {
            Type = ExtractType(schemaDict),
            Description = ExtractDescription(schemaDict),
            Rules = ExtractValidationRules(schemaDict),
            EnumValues = ExtractEnum(schemaDict) ?? []
        };
    }

    private async Task<List<SchemaNode>> BuildSchemaListAsync(
        List<object?> schemaList,
        string documentKey,
        CancellationToken ct)
    {
        var result = new List<SchemaNode>();

        foreach (var item in schemaList)
        {
            if (item is Dictionary<string, object?> itemDict)
            {
                var node = await BuildSchemaNodeFromInlineAsync(itemDict, documentKey, ct);
                result.Add(node);
            }
        }

        return result;
    }

    private async Task<Dictionary<string, SchemaNode>> ExtractPropertiesAsync(
        Dictionary<string, object?> schemaDict,
        string documentKey,
        CancellationToken ct)
    {
        var result = new Dictionary<string, SchemaNode>();

        if (!schemaDict.TryGetValue("properties", out var propsObj) || propsObj is not Dictionary<string, object?> props)
            return result;

        foreach (var (propName, propSchema) in props)
        {
            if (propSchema is Dictionary<string, object?> propDict)
            {
                var propNode = await BuildSchemaNodeFromInlineAsync(propDict, documentKey, ct);
                result[propName] = propNode;
            }
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extractors — вспомогательные методы
    // ─────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> ExtractSchemas(Dictionary<string, object?> doc)
    {
        if (doc.TryGetValue("components", out var compObj) && compObj is Dictionary<string, object?> comp &&
            comp.TryGetValue("schemas", out var schemasObj) && schemasObj is Dictionary<string, object?> schemas)
            return schemas;

        return [];
    }

    private static string? ExtractType(Dictionary<string, object?> schema)
        => schema.TryGetValue("type", out var typeObj) ? typeObj as string : null;

    private static string? ExtractDescription(Dictionary<string, object?> schema)
        => schema.TryGetValue("description", out var descObj) ? descObj as string : null;

    private static List<string>? ExtractEnum(Dictionary<string, object?> schema)
    {
        if (!schema.TryGetValue("enum", out var enumObj) || enumObj is not List<object?> enumList)
            return null;

        return enumList
            .OfType<string>()
            .ToList();
    }

    private static List<string> ExtractRequired(Dictionary<string, object?> schema)
    {
        if (!schema.TryGetValue("required", out var reqObj) || reqObj is not List<object?> reqList)
            return [];

        return reqList
            .OfType<string>()
            .ToList();
    }

    private static ValidationRules ExtractValidationRules(Dictionary<string, object?> schema)
    {
        return new ValidationRules
        {
            MinLength = ExtractInt(schema, "minLength"),
            MaxLength = ExtractInt(schema, "maxLength"),
            Pattern = schema.TryGetValue("pattern", out var p) ? p as string : null,
            Minimum = ExtractDouble(schema, "minimum"),
            Maximum = ExtractDouble(schema, "maximum"),
            ExclusiveMinimum = ExtractBool(schema, "exclusiveMinimum"),
            ExclusiveMaximum = ExtractBool(schema, "exclusiveMaximum"),
            MinItems = ExtractInt(schema, "minItems"),
            MaxItems = ExtractInt(schema, "maxItems"),
            UniqueItems = ExtractBool(schema, "uniqueItems"),
            Format = schema.TryGetValue("format", out var f) ? f as string : null,
            Nullable = ExtractBool(schema, "nullable"),
        };
    }

    private static string? ExtractPropertyDescription(Dictionary<string, object?> schema, string propertyName)
    {
        if (!schema.TryGetValue("properties", out var propsObj) || propsObj is not Dictionary<string, object?> props)
            return null;

        if (props.TryGetValue(propertyName, out var propObj) && propObj is Dictionary<string, object?> prop)
            return prop.TryGetValue("description", out var desc) ? desc as string : null;

        return null;
    }

    private static (string? propertyName, IReadOnlyDictionary<string, string> mapping)
        ExtractDiscriminator(Dictionary<string, object?> schema, List<object?> oneOfList)
    {
        if (!schema.TryGetValue("discriminator", out var discObj) || discObj is not Dictionary<string, object?> disc)
            return (null, new Dictionary<string, string>());

        var propertyName = disc.TryGetValue("propertyName", out var pn) ? pn as string : null;
        var mapping = new Dictionary<string, string>();

        if (disc.TryGetValue("mapping", out var mapObj) && mapObj is Dictionary<string, object?> mapDict)
        {
            foreach (var (key, value) in mapDict)
            {
                if (value is string refString)
                {
                    // Извлекаем имя схемы из $ref
                    var schemaName = refString.Split('/').LastOrDefault() ?? refString;
                    mapping[key] = schemaName;
                }
            }
        }

        return (propertyName, mapping);
    }

    // ── Helper для извлечения типизированных значений ────────────────────
    private static int? ExtractInt(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            int i => i,
            long l => (int)l,
            _ => null
        };
    }

    private static double? ExtractDouble(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            double d => d,
            int i => (double)i,
            long l => (double)l,
            _ => null
        };
    }

    private static bool ExtractBool(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return false;

        return value switch
        {
            bool b => b,
            _ => false
        };
    }
}
