using OpenApiDtoGenerator.Generators.Masking;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.Masking;

/// <summary>
/// TDD: MaskingStrategyExecutor — применение стратегий к значениям.
///
/// Проверяем, что каждая стратегия правильно преобразует строку в маскированную версию.
/// Это runtime-логика, которая пойдёт в сгенерированную библиотеку.
/// </summary>
public class MaskingStrategyExecutorTests
{
    private readonly MaskingStrategyExecutor _sut = new();

    // ═══════════════════════════════════════════════════════════════
    // FULL
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_Full_ReturnsAsterisks()
    {
        _sut.Apply("SecretValue", MaskStrategy.Full).ShouldBe("***");
    }

    [Fact]
    public void Apply_Full_EmptyString_ReturnsEmpty()
    {
        _sut.Apply("", MaskStrategy.Full).ShouldBe("");
    }

    [Fact]
    public void Apply_Full_Null_ReturnsNull()
    {
        _sut.Apply(null, MaskStrategy.Full).ShouldBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // PARTIAL
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_Partial_ShowsFirstAndLast()
    {
        // "771234567890" (12 chars) → visibleStart=3, visibleEnd=2 → "771*******90"
        var result = _sut.Apply("771234567890", MaskStrategy.Partial, visibleStart: 3, visibleEnd: 2);
        result.ShouldBe("771*******90");
    }

    [Fact]
    public void Apply_Partial_Passport_MasksMiddle()
    {
        // "4510" (4 chars) → visibleStart=2, visibleEnd=2 → "4510" (полностью видно, т.к. 2+2>=4)
        // Согласно правилу: если visible >= length — вернуть маску без открытых
        var result = _sut.Apply("4510", MaskStrategy.Partial, visibleStart: 2, visibleEnd: 2);
        // 2+2 = 4, а длина 4 → полностью маскируем (нельзя показать всё)
        result.ShouldBe("****");
    }

    [Fact]
    public void Apply_Partial_ShortString_FullyMasked()
    {
        // Строка короче visible+visible → маскируем полностью
        var result = _sut.Apply("ab", MaskStrategy.Partial, visibleStart: 2, visibleEnd: 2);
        result.ShouldBe("**");
    }

    [Fact]
    public void Apply_Partial_LongString_MaskedMiddle()
    {
        // "1234567890" (10 chars), visibleStart=2, visibleEnd=2 → "12******90"
        var result = _sut.Apply("1234567890", MaskStrategy.Partial, visibleStart: 2, visibleEnd: 2);
        result.ShouldBe("12******90");
    }

    // ═══════════════════════════════════════════════════════════════
    // FIRST CHAR
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_FirstChar_ShowsOnlyFirstChar()
    {
        _sut.Apply("Иванов", MaskStrategy.FirstChar).ShouldBe("И*****");
    }

    [Fact]
    public void Apply_FirstChar_SingleChar_UnchangedButNoMask()
    {
        _sut.Apply("И", MaskStrategy.FirstChar).ShouldBe("И");
    }

    [Fact]
    public void Apply_FirstChar_EmptyString_ReturnsEmpty()
    {
        _sut.Apply("", MaskStrategy.FirstChar).ShouldBe("");
    }

    // ═══════════════════════════════════════════════════════════════
    // YEAR ONLY (для дат)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_YearOnly_ISODate_KeepsYear()
    {
        _sut.Apply("1990-05-15", MaskStrategy.YearOnly).ShouldBe("1990-**-**");
    }

    [Fact]
    public void Apply_YearOnly_ISODateTime_KeepsYear()
    {
        _sut.Apply("1990-05-15T10:30:00Z", MaskStrategy.YearOnly).ShouldBe("1990-**-**");
    }

    [Fact]
    public void Apply_YearOnly_InvalidDate_ReturnsMasked()
    {
        // Если не удалось распарсить дату — маскируем полностью
        _sut.Apply("not-a-date", MaskStrategy.YearOnly).ShouldBe("***");
    }

    // ═══════════════════════════════════════════════════════════════
    // HASH
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_Hash_ReturnsHexString()
    {
        var result = _sut.Apply("SecretValue", MaskStrategy.Hash);
        result.ShouldNotBeNull();
        result!.Length.ShouldBeGreaterThan(0);
        // Хеш должен состоять только из hex-символов
        result.ShouldMatch("^[0-9a-f]+$");
    }

    [Fact]
    public void Apply_Hash_SameInput_SameOutput()
    {
        var first  = _sut.Apply("test", MaskStrategy.Hash);
        var second = _sut.Apply("test", MaskStrategy.Hash);
        first.ShouldBe(second);
    }

    [Fact]
    public void Apply_Hash_DifferentInputs_DifferentOutputs()
    {
        var a = _sut.Apply("valueA", MaskStrategy.Hash);
        var b = _sut.Apply("valueB", MaskStrategy.Hash);
        a.ShouldNotBe(b);
    }

    // ═══════════════════════════════════════════════════════════════
    // NONE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_None_ReturnsOriginal()
    {
        _sut.Apply("original", MaskStrategy.None).ShouldBe("original");
    }
}
