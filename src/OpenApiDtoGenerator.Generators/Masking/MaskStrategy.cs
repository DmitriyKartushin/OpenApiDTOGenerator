namespace OpenApiDtoGenerator.Generators.Masking;

/// <summary>
/// Стратегии маскировки строковых значений.
/// </summary>
public enum MaskStrategy
{
    /// <summary>Без маскировки — значение возвращается как есть.</summary>
    None,

    /// <summary>Всё значение → "***".</summary>
    Full,

    /// <summary>Видны первые VisibleStart и последние VisibleEnd символов.</summary>
    Partial,

    /// <summary>Только первый символ, остальное звёздочками ("Иванов" → "И*****").</summary>
    FirstChar,

    /// <summary>Для дат: сохраняется только год ("1990-05-15" → "1990-**-**").</summary>
    YearOnly,

    /// <summary>SHA-256 хеш значения в hex-виде (первые 16 символов).</summary>
    Hash
}
