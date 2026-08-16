using Shouldly;

namespace OpenApiDtoGenerator.Core.Tests;

/// <summary>
/// Дополнительные Shouldly-расширения для типов, которые не поддерживаются
/// стандартным Shouldly 4.x из коробки.
///
/// Проблема: Shouldly.ShouldContainKey определён только для IDictionary&lt;K,V&gt;,
/// но не для IReadOnlyDictionary&lt;K,V&gt;.
/// Решение: расширяем через IEnumerable&lt;KeyValuePair&lt;K,V&gt;&gt;.
/// </summary>
public static class ShouldlyExtensions
{
    /// <summary>
    /// Проверяет, что словарь содержит указанный ключ.
    /// Работает с любым словарём: Dictionary, IReadOnlyDictionary, ImmutableDictionary.
    /// </summary>
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
