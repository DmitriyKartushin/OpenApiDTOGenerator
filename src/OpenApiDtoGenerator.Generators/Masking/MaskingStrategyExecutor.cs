using System.Security.Cryptography;
using System.Text;

namespace OpenApiDtoGenerator.Generators.Masking;

/// <summary>
/// Runtime-исполнитель стратегий маскировки: строка + стратегия → маскированная строка.
///
/// Тот же класс генерируется в run-time-библиотеку сгенерированного проекта.
/// Логика здесь и там должна совпадать 1-в-1 (source of truth).
/// </summary>
public sealed class MaskingStrategyExecutor
{
    /// <summary>
    /// Применяет стратегию маскировки к значению.
    /// </summary>
    public string? Apply(string? value, MaskStrategy strategy, int visibleStart = 0, int visibleEnd = 0)
    {
        if (value is null)
            return null;

        return strategy switch
        {
            MaskStrategy.None      => value,
            MaskStrategy.Full      => ApplyFull(value),
            MaskStrategy.Partial   => ApplyPartial(value, visibleStart, visibleEnd),
            MaskStrategy.FirstChar => ApplyFirstChar(value),
            MaskStrategy.YearOnly  => ApplyYearOnly(value),
            MaskStrategy.Hash      => ApplyHash(value),
            _                       => value
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Стратегии
    // ─────────────────────────────────────────────────────────────────────────

    private static string ApplyFull(string value)
        => value.Length == 0 ? string.Empty : "***";

    private static string ApplyPartial(string value, int visibleStart, int visibleEnd)
    {
        if (value.Length == 0)
            return value;

        // Если суммарно видимых символов >= длины строки — маскируем полностью
        if (visibleStart + visibleEnd >= value.Length)
            return new string('*', value.Length);

        var start = value[..visibleStart];
        var end = value[^visibleEnd..];
        var middleLength = value.Length - visibleStart - visibleEnd;

        return start + new string('*', middleLength) + end;
    }

    private static string ApplyFirstChar(string value)
    {
        if (value.Length == 0) return string.Empty;
        if (value.Length == 1) return value;
        return value[0] + new string('*', value.Length - 1);
    }

    private static string ApplyYearOnly(string value)
    {
        // Ожидаем ISO дату: 1990-05-15 или 1990-05-15T10:30:00Z
        if (value.Length >= 4 && value[4] == '-' && int.TryParse(value.AsSpan(0, 4), out _))
        {
            return $"{value[..4]}-**-**";
        }

        // Не удалось распарсить как дату → полная маскировка
        return "***";
    }

    private static string ApplyHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        // Возвращаем первые 16 hex-символов (8 байт) — компактно и достаточно для сравнения
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
