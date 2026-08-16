namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Исключение при обнаружении циклической $ref ссылки.
///
/// Пример: SchemaA → SchemaB → SchemaC → SchemaA (цикл!)
/// </summary>
public sealed class CyclicReferenceException : Exception
{
    /// <summary>Ключ ($ref), который уже находится в стеке обхода.</summary>
    public string CyclicKey { get; }

    /// <summary>
    /// Полный путь обхода на момент обнаружения цикла.
    /// Элементы упорядочены от корня к точке обнаружения.
    /// Например: ["SchemaA", "SchemaB", "SchemaC", "SchemaA"]
    /// </summary>
    public IReadOnlyList<string> CyclePath { get; }

    public CyclicReferenceException(string cyclicKey, IReadOnlyList<string> cyclePath)
        : base(BuildMessage(cyclicKey, cyclePath))
    {
        CyclicKey = cyclicKey;
        CyclePath = cyclePath;
    }

    private static string BuildMessage(string cyclicKey, IReadOnlyList<string> path)
    {
        var chain = string.Join(" → ", path) + $" → {cyclicKey} (CYCLE)";
        return $"Circular $ref detected for '{cyclicKey}'. Resolution path: {chain}";
    }
}
