namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Формат содержимого загруженного документа.
/// </summary>
public enum DocumentFormat
{
    Yaml,
    Json
}

/// <summary>
/// Загруженный и закешированный документ (raw строка + формат).
/// Разбор в дерево происходит отдельно — в IRBuilder.
/// </summary>
public sealed class CachedDocument
{
    public string RawContent { get; }
    public DocumentFormat Format { get; }

    public CachedDocument(string rawContent, DocumentFormat format)
    {
        RawContent = rawContent;
        Format = format;
    }
}
