namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Контракт загрузчика документа по нормализованному ключу (путь или URI).
/// Каждый тип источника имеет свою реализацию:
///   — <see cref="LocalFileLoader"/>   для файловой системы
///   — RemoteHttpLoader                для HTTP/HTTPS
///   — EmbeddedLoader                  для тестов (in-memory)
/// </summary>
public interface IDocumentLoader
{
    /// <summary>
    /// Загружает raw-содержимое документа (YAML или JSON строку).
    /// </summary>
    /// <param name="normalizedKey">
    ///   Нормализованный ключ:
    ///   — абсолютный путь на диске  ("/home/user/spec.yaml")
    ///   — нормализованный URI       ("https://example.com/spec.yaml")
    /// </param>
    /// <param name="ct">Токен отмены.</param>
    Task<string> LoadAsync(string normalizedKey, CancellationToken ct);
}
