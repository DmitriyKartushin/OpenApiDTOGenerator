namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// RAII-обёртка над CycleDetector.
/// Автоматически вызывает Leave при выходе из using-блока.
///
/// Использование:
/// <code>
/// using (detector.Scope("#/components/schemas/PersonInfo"))
/// {
///     // обрабатываем схему
/// }  // ← Leave вызывается автоматически
/// </code>
/// </summary>
public readonly struct CycleDetectorScope : IDisposable
{
    private readonly CycleDetector _detector;
    private readonly string _key;

    internal CycleDetectorScope(CycleDetector detector, string key)
    {
        _detector = detector;
        _key = key;
        detector.Enter(key); // Enter при создании scope
    }

    /// <summary>Вызывает Leave автоматически при выходе из using.</summary>
    public void Dispose() => _detector.Leave(_key);
}

/// <summary>
/// Extension-метод для создания scope.
/// </summary>
public static class CycleDetectorExtensions
{
    /// <summary>
    /// Входит в обход ключа и возвращает scope,
    /// который автоматически вызовет Leave при Dispose.
    /// </summary>
    /// <exception cref="CyclicReferenceException">Если ключ уже в стеке.</exception>
    public static CycleDetectorScope Scope(this CycleDetector detector, string key)
        => new(detector, key);
}
