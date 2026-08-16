namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Реализует навигацию по дереву документа (Dictionary/List) по RFC 6901 JSON Pointer.
///
/// Документ представлен как дерево объектов C#:
///   объект → Dictionary&lt;string, object?&gt;
///   массив → List&lt;object?&gt;
///   скаляр → string, int, double, bool, null
///
/// Escape-последовательности RFC 6901:
///   ~1 → /   (слеш в имени ключа)
///   ~0 → ~   (тильда в имени ключа)
///   Порядок важен: сначала ~1, потом ~0.
/// </summary>
public sealed class JsonPointerResolver
{
    /// <summary>
    /// Разрешает JSON Pointer и возвращает найденный узел.
    /// Бросает <see cref="JsonPointerException"/> при ошибке навигации.
    /// </summary>
    /// <param name="document">Корень документа (Dictionary или List).</param>
    /// <param name="pointer">JSON Pointer (например "/components/schemas/Foo").</param>
    /// <returns>Найденный узел дерева.</returns>
    public object? Resolve(object? document, string pointer)
    {
        ArgumentNullException.ThrowIfNull(document, nameof(document));
        ArgumentNullException.ThrowIfNull(pointer, nameof(pointer));

        // Пустой pointer и "/" → корень документа
        if (pointer.Length == 0 || pointer == "/")
            return document;

        var tokens = ParseTokens(pointer);
        return Navigate(document, tokens, pointer);
    }

    /// <summary>
    /// Пытается разрешить JSON Pointer без бросания исключения.
    /// </summary>
    public bool TryResolve(object? document, string pointer, out object? value)
    {
        try
        {
            value = Resolve(document, pointer);
            return true;
        }
        catch (Exception ex) when (ex is JsonPointerException or ArgumentNullException)
        {
            value = null;
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core navigation
    // ─────────────────────────────────────────────────────────────────────────

    private static object? Navigate(object? current, string[] tokens, string fullPointer)
    {
        foreach (var rawToken in tokens)
        {
            var token = Unescape(rawToken);

            switch (current)
            {
                // ── Объект: ищем ключ ────────────────────────────────────────
                case Dictionary<string, object?> dict:
                    if (!dict.TryGetValue(token, out current))
                        throw new JsonPointerException(fullPointer, token,
                            $"Key '{token}' not found in object. " +
                            $"Available keys: [{string.Join(", ", dict.Keys)}]");
                    break;

                // ── Массив: ищем по индексу ──────────────────────────────────
                case List<object?> list:
                    if (!int.TryParse(token, out var index))
                        throw new JsonPointerException(fullPointer, token,
                            $"Cannot index array with non-numeric token '{token}'.");

                    if (index < 0 || index >= list.Count)
                        throw new JsonPointerException(fullPointer, token,
                            $"Array index {index} is out of bounds (length: {list.Count}).");

                    current = list[index];
                    break;

                // ── Скаляр: нельзя идти глубже ──────────────────────────────
                case null:
                    throw new JsonPointerException(fullPointer, token,
                        "Cannot navigate into a null node.");

                default:
                    throw new JsonPointerException(fullPointer, token,
                        $"Cannot navigate into a scalar value of type '{current.GetType().Name}'.");
            }
        }

        return current;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Разбирает pointer вида "/a/b/c" на токены ["a", "b", "c"].
    /// Ведущий '/' отрезается, пустые токены после него игнорируются.
    /// </summary>
    private static string[] ParseTokens(string pointer)
    {
        // "/a/b/c" → ["a", "b", "c"]
        var withoutLeadingSlash = pointer.StartsWith('/')
            ? pointer[1..]
            : pointer;

        return withoutLeadingSlash.Split('/');
    }

    /// <summary>
    /// Применяет RFC 6901 escape:
    ///   ~1 → /
    ///   ~0 → ~
    /// Порядок важен: сначала ~1, потом ~0 (иначе ~01 → ~/ вместо ~1).
    /// </summary>
    private static string Unescape(string token)
        => token.Replace("~1", "/").Replace("~0", "~");
}
