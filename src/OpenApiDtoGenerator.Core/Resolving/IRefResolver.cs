using OpenApiDtoGenerator.Core.Models;

namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Контракт главного резолвера $ref ссылок.
/// Оркестрирует RefParser → DocumentLoader → JsonPointerResolver → CycleDetector.
/// </summary>
public interface IRefResolver
{
    /// <summary>
    /// Резолвит строку $ref в SchemaNode.
    /// </summary>
    /// <param name="refString">Значение $ref из YAML/JSON.</param>
    /// <param name="currentDocumentKey">
    ///   Нормализованный ключ текущего документа (нужен для резолвинга относительных путей).
    /// </param>
    /// <param name="ct">Токен отмены.</param>
    Task<SchemaNode> ResolveAsync(
        string refString,
        string currentDocumentKey,
        CancellationToken ct = default);
}
