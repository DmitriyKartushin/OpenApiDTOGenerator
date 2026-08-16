namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Загружает содержимое файла с диска по абсолютному пути.
/// Реализует <see cref="IDocumentLoader"/> для локальных файлов.
/// </summary>
public sealed class LocalFileLoader : IDocumentLoader
{
    /// <summary>
    /// Загружает содержимое файла асинхронно.
    /// </summary>
    /// <param name="normalizedKey">Абсолютный путь к файлу на диске.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Строковое содержимое файла (UTF-8).</returns>
    /// <exception cref="ArgumentException">Путь пустой или null.</exception>
    /// <exception cref="FileNotFoundException">Файл не найден.</exception>
    /// <exception cref="InvalidOperationException">Путь указывает на директорию.</exception>
    /// <exception cref="OperationCanceledException">Отмена через CancellationToken.</exception>
    public async Task<string> LoadAsync(string normalizedKey, CancellationToken ct)
    {
        // ── Guard ────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(normalizedKey))
            throw new ArgumentException("File path must not be null or whitespace.", nameof(normalizedKey));

        // Проверяем до чтения, чтобы дать понятное сообщение
        if (Directory.Exists(normalizedKey))
            throw new InvalidOperationException(
                $"The path '{normalizedKey}' points to a directory, not a file.");

        if (!File.Exists(normalizedKey))
            throw new FileNotFoundException(
                $"OpenAPI document file not found: '{normalizedKey}'", normalizedKey);

        // ── Чтение ──────────────────────────────────────────────────────────
        ct.ThrowIfCancellationRequested();

        return await File.ReadAllTextAsync(normalizedKey, System.Text.Encoding.UTF8, ct);
    }
}
