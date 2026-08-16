using Shouldly;

namespace OpenApiDtoGenerator.Integration.Tests;

/// <summary>
/// Shouldly-расширения для IReadOnlyDictionary.
/// Shouldly 4.x определяет ShouldContainKey только для IDictionary&lt;K,V&gt;,
/// поэтому добавляем перегрузку для IReadOnlyDictionary&lt;K,V&gt;.
/// </summary>
public static class ShouldlyExtensions
{
    public static void ShouldContainKey<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dict,
        TKey key,
        string? customMessage = null)
    {
        dict.ContainsKey(key).ShouldBeTrue(
            customMessage ?? $"Dictionary should contain key '{key}', but it does not. " +
                             $"Available keys: [{string.Join(", ", dict.Keys)}]");
    }
}
