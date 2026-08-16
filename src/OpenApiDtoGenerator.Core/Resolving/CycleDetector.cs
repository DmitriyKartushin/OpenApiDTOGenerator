namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Отслеживает стек активных $ref резолвингов для обнаружения циклов.
///
/// Принцип работы:
///   - Enter(key) — входим в обработку схемы с данным ключом
///     → если ключ уже в стеке — цикл → CyclicReferenceException
///   - Leave(key) — завершаем обработку, убираем из стека
///   - IsVisiting(key) — проверяем, находится ли ключ в текущем стеке
///
/// Один экземпляр = один контекст резолвинга (один поток обхода).
/// Для параллельных веток создавать отдельные экземпляры.
///
/// Структуры данных:
///   - List&lt;string&gt; _stack — сохраняет порядок (путь от корня)
///   - HashSet&lt;string&gt; _visiting — O(1) проверка принадлежности
/// </summary>
public sealed class CycleDetector
{
    // Стек для сохранения порядка пути (от корня к текущему узлу)
    private readonly List<string> _stack = [];

    // HashSet для O(1) проверки IsVisiting
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Помечает ключ как «в процессе обхода».
    /// </summary>
    /// <param name="key">Нормализованный ключ схемы (обычно $ref строка).</param>
    /// <exception cref="ArgumentException">Ключ пустой или null.</exception>
    /// <exception cref="CyclicReferenceException">Ключ уже в стеке — цикл обнаружен.</exception>
    public void Enter(string key)
    {
        ValidateKey(key);

        if (_visiting.Contains(key))
        {
            // Строим путь цикла: текущий стек + повторяющийся ключ
            var cyclePath = new List<string>(_stack) { key };
            throw new CyclicReferenceException(key, cyclePath);
        }

        _stack.Add(key);
        _visiting.Add(key);
    }

    /// <summary>
    /// Снимает пометку — обход данного ключа завершён.
    /// Должен вызываться строго в порядке LIFO (соответствующий Enter).
    /// </summary>
    /// <exception cref="ArgumentException">Ключ пустой или null.</exception>
    /// <exception cref="InvalidOperationException">
    ///   Ключ не является верхушкой стека или отсутствует в стеке.
    /// </exception>
    public void Leave(string key)
    {
        ValidateKey(key);

        if (_stack.Count == 0 || !_visiting.Contains(key))
            throw new InvalidOperationException(
                $"Cannot leave '{key}': key is not in the visiting stack.");

        // Проверяем что снимаем именно верхушку стека (LIFO-порядок)
        var top = _stack[^1];
        if (top != key)
            throw new InvalidOperationException(
                $"Cannot leave '{key}': expected to leave '{top}' first (LIFO order). " +
                $"Current stack: [{string.Join(" → ", _stack)}]");

        _stack.RemoveAt(_stack.Count - 1);
        _visiting.Remove(key);
    }

    /// <summary>
    /// Возвращает true если ключ сейчас находится в стеке обхода.
    /// </summary>
    public bool IsVisiting(string key) => _visiting.Contains(key);

    /// <summary>
    /// Снимок текущего стека от корня к вершине (FIFO-порядок, копия).
    /// Безопасно читать в любой момент — не меняется при последующих Enter/Leave.
    /// </summary>
    public IReadOnlyList<string> VisitingStack => _stack.ToList();

    /// <summary>
    /// Полностью сбрасывает состояние детектора.
    /// Позволяет переиспользовать экземпляр после завершения обхода.
    /// </summary>
    public void Reset()
    {
        _stack.Clear();
        _visiting.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Cycle detector key must not be null or empty.", nameof(key));
    }
}
