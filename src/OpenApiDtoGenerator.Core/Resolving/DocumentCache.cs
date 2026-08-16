using System.Collections.Concurrent;

namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Потокобезопасный кеш загруженных документов.
///
/// Ключ — нормализованная строка:
///   — абсолютный путь на диске  (Path.GetFullPath)
///   — нормализованный URI       (new Uri(x).ToString())
///
/// HTTP URI нормализуются к нижнему регистру схемы+хоста.
/// Файловые пути чувствительны к регистру на Linux и
/// нечувствительны на Windows (определяется автоматически).
/// </summary>
public sealed class DocumentCache
{
    // SemaphoreSlim per-key для атомарного GetOrAdd без двойной загрузки
    private readonly ConcurrentDictionary<string, CachedDocument> _store;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public DocumentCache()
    {
        // На Windows файловые пути нечувствительны к регистру,
        // но URI всегда нормализуем отдельно в NormalizeKey
        _store = new ConcurrentDictionary<string, CachedDocument>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
    }

    /// <summary>Количество записей в кеше.</summary>
    public int Count => _store.Count;

    /// <summary>
    /// Пытается получить документ по ключу.
    /// </summary>
    public bool TryGet(string key, out CachedDocument? doc)
    {
        ValidateKey(key);
        var normalizedKey = NormalizeKey(key);
        return _store.TryGetValue(normalizedKey, out doc);
    }

    /// <summary>
    /// Сохраняет документ в кеш. Перезаписывает если ключ уже есть.
    /// </summary>
    public void Set(string key, CachedDocument document)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(document);

        var normalizedKey = NormalizeKey(key);
        _store[normalizedKey] = document;
    }

    /// <summary>
    /// Атомарно возвращает документ из кеша или создаёт через фабрику.
    /// Гарантирует: фабрика вызывается не более одного раза на ключ.
    /// </summary>
    public async Task<CachedDocument> GetOrAddAsync(
        string key,
        Func<string, Task<CachedDocument>> factory,
        CancellationToken ct = default)
    {
        ValidateKey(key);
        var normalizedKey = NormalizeKey(key);

        // Быстрый путь — уже есть в кеше
        if (_store.TryGetValue(normalizedKey, out var existing))
            return existing;

        // Получаем или создаём семафор для данного ключа
        var semaphore = _locks.GetOrAdd(normalizedKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            // Double-check после захвата блокировки
            if (_store.TryGetValue(normalizedKey, out existing))
                return existing;

            var doc = await factory(normalizedKey);
            _store[normalizedKey] = doc;
            return doc;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Очищает кеш полностью.</summary>
    public void Clear()
    {
        _store.Clear();
        _locks.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Нормализует ключ: для URI приводит схему и хост к нижнему регистру.
    /// Файловые пути остаются как есть (регистр определяется OS).
    /// </summary>
    private static string NormalizeKey(string key)
    {
        // HTTP/HTTPS URI — нормализуем через Uri чтобы схема+хост были lowercase
        if (key.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(key).ToString().TrimEnd('/');
        }

        return key;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key must not be null or whitespace.", nameof(key));
    }
}
