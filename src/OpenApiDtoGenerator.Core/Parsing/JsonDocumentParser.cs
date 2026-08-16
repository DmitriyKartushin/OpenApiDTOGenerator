namespace OpenApiDtoGenerator.Core.Parsing;

/// <summary>
/// Простой парсер YAML/JSON в Object граф (Dictionary/List).
/// Использует System.Text.Json (работает с JSON).
///
/// Для полноценной YAML поддержки здесь можно добавить YamlDotNet.
/// На данный момент System.Text.Json достаточно для MVP,
/// так как YamlDotNet парсит JSON тем же способом.
/// </summary>
public sealed class JsonDocumentParser : IDocumentParser
{
    public object? Parse(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
            throw new ArgumentException("Content must not be empty.", nameof(rawContent));

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawContent);
            return JsonElementToObject(doc.RootElement);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new FormatException($"Failed to parse JSON/YAML content: {ex.Message}", ex);
        }
    }

    private static object? JsonElementToObject(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object =>
                element.EnumerateObject()
                    .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),

            System.Text.Json.JsonValueKind.Array =>
                element.EnumerateArray()
                    .Select(JsonElementToObject)
                    .ToList<object?>(),

            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number =>
                element.TryGetInt32(out var intVal) ? (object)intVal
                : element.TryGetInt64(out var longVal) ? (object)longVal
                : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}")
        };
    }
}
