using YamlDotNet.RepresentationModel;

namespace OpenApiDtoGenerator.Core.Parsing;

/// <summary>
/// Парсит YAML (и валидный JSON, так как JSON — подмножество YAML)
/// в Object граф (Dictionary&lt;string, object?&gt; / List&lt;object?&gt; / скаляры).
///
/// Использует YamlDotNet.RepresentationModel (YamlStream) для полного контроля
/// над типами скаляров — важно для сохранения "quoted numeric strings"
/// как строк (например series: '4510' должно остаться строкой "4510",
/// а не превратиться в число 4510).
/// </summary>
public sealed class YamlDocumentParser : IDocumentParser
{
    public object? Parse(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
            throw new ArgumentException("Content must not be empty.", nameof(rawContent));

        try
        {
            var yamlStream = new YamlStream();
            using var reader = new StringReader(rawContent);
            yamlStream.Load(reader);

            if (yamlStream.Documents.Count == 0)
                return null;

            var rootNode = yamlStream.Documents[0].RootNode;
            return ConvertNode(rootNode);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new FormatException($"Failed to parse YAML content: {ex.Message}", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Node conversion
    // ─────────────────────────────────────────────────────────────────────────

    private static object? ConvertNode(YamlNode node)
    {
        return node switch
        {
            YamlMappingNode mapping => ConvertMapping(mapping),
            YamlSequenceNode sequence => ConvertSequence(sequence),
            YamlScalarNode scalar => ConvertScalar(scalar),
            _ => throw new InvalidOperationException($"Unsupported YAML node type: {node.GetType().Name}")
        };
    }

    private static Dictionary<string, object?> ConvertMapping(YamlMappingNode mapping)
    {
        var result = new Dictionary<string, object?>();

        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? string.Empty;
            result[key] = ConvertNode(valueNode);
        }

        return result;
    }

    private static List<object?> ConvertSequence(YamlSequenceNode sequence)
    {
        return sequence.Children.Select(ConvertNode).ToList();
    }

    /// <summary>
    /// Преобразует скалярный YAML-узел в C# тип.
    /// Учитывает YAML-стиль узла: если значение было в кавычках (Style != Plain),
    /// оно ВСЕГДА остаётся строкой, даже если выглядит как число (например '4510').
    /// </summary>
    private static object? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;

        if (value is null)
            return null;

        // Если значение было в кавычках — это принудительно строка,
        // не пытаемся угадать тип (важно для '4510', "true", etc.)
        var isQuoted = scalar.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted
                                     or YamlDotNet.Core.ScalarStyle.DoubleQuoted;

        if (isQuoted)
            return value;

        // Plain style — пытаемся определить тип по содержимому (YAML 1.1 core schema)
        return InferPlainScalarType(value);
    }

    private static object? InferPlainScalarType(string value)
    {
        // null
        if (value is "null" or "~" or "")
            return null;

        // boolean
        if (value is "true" or "True" or "TRUE")
            return true;
        if (value is "false" or "False" or "FALSE")
            return false;

        // integer
        if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var intVal))
            return intVal;

        if (long.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var longVal))
            return longVal;

        // double
        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
            return doubleVal;

        // строка по умолчанию
        return value;
    }
}
