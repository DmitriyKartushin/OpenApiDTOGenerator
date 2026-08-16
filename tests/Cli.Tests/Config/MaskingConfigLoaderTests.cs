using OpenApiDtoGenerator.Cli.Config;
using OpenApiDtoGenerator.Generators.Masking;
using Shouldly;

namespace OpenApiDtoGenerator.Cli.Tests.Config;

/// <summary>
/// TDD: MaskingConfigLoader — парсинг YAML-конфига в MaskingConfig.
/// </summary>
public class MaskingConfigLoaderTests
{
    private readonly MaskingConfigLoader _sut = new();

    // ═══════════════════════════════════════════════════════════════
    // Формат: masking:\n  rules: ...
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LoadFromString_WithMaskingRoot_ParsesRules()
    {
        const string yaml = """
        masking:
          rules:
            - fields: [series, number]
              strategy: partial
              visibleStart: 2
              visibleEnd: 2
        """;

        var result = _sut.LoadFromString(yaml);

        result.Rules.Count.ShouldBe(1);
        result.Rules[0].Strategy.ShouldBe(MaskStrategy.Partial);
        result.Rules[0].Fields.Count.ShouldBe(2);
        result.Rules[0].VisibleStart.ShouldBe(2);
        result.Rules[0].VisibleEnd.ShouldBe(2);
    }

    [Fact]
    public void LoadFromString_TopLevelRules_AlsoWorks()
    {
        // Поддерживаем упрощённый формат — без обёртки "masking:"
        const string yaml = """
        rules:
          - fields: [inn]
            strategy: full
        """;

        var result = _sut.LoadFromString(yaml);
        result.Rules.Count.ShouldBe(1);
        result.Rules[0].Strategy.ShouldBe(MaskStrategy.Full);
    }

    // ═══════════════════════════════════════════════════════════════
    // Все стратегии парсятся
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("full",       MaskStrategy.Full)]
    [InlineData("partial",    MaskStrategy.Partial)]
    [InlineData("firstChar",  MaskStrategy.FirstChar)]
    [InlineData("yearOnly",   MaskStrategy.YearOnly)]
    [InlineData("hash",       MaskStrategy.Hash)]
    [InlineData("none",       MaskStrategy.None)]
    public void LoadFromString_AllStrategies_ParsedCaseInsensitive(string strategyStr, MaskStrategy expected)
    {
        var yaml = $"""
        rules:
          - fields: [x]
            strategy: {strategyStr}
        """;

        var result = _sut.LoadFromString(yaml);
        result.Rules[0].Strategy.ShouldBe(expected);
    }

    // ═══════════════════════════════════════════════════════════════
    // Несколько правил
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LoadFromString_MultipleRules_AllParsed()
    {
        const string yaml = """
        masking:
          rules:
            - fields: [series]
              strategy: partial
              visibleStart: 2
              visibleEnd: 2
            - fields: [lastName, firstName, middleName]
              strategy: firstChar
            - fields: [issuedBy]
              strategy: full
        """;

        var result = _sut.LoadFromString(yaml);

        result.Rules.Count.ShouldBe(3);
        result.FindRuleForField("series")!.Strategy.ShouldBe(MaskStrategy.Partial);
        result.FindRuleForField("firstName")!.Strategy.ShouldBe(MaskStrategy.FirstChar);
        result.FindRuleForField("issuedBy")!.Strategy.ShouldBe(MaskStrategy.Full);
    }

    // ═══════════════════════════════════════════════════════════════
    // ERROR CASES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LoadFromFile_MissingFile_ThrowsFileNotFoundException()
    {
        Should.Throw<FileNotFoundException>(
            () => _sut.LoadFromFile("/tmp/definitely-does-not-exist.yaml"));
    }

    [Fact]
    public void LoadFromString_UnknownStrategy_ThrowsFormatException()
    {
        const string yaml = """
        rules:
          - fields: [x]
            strategy: bogusStrategy
        """;

        var ex = Should.Throw<FormatException>(() => _sut.LoadFromString(yaml));
        ex.Message.ShouldContain("bogusStrategy");
    }

    [Fact]
    public void LoadFromString_MissingRules_ThrowsFormatException()
    {
        const string yaml = "masking:\n  otherKey: value";
        Should.Throw<FormatException>(() => _sut.LoadFromString(yaml));
    }

    [Fact]
    public void LoadFromString_MissingFields_ThrowsFormatException()
    {
        const string yaml = """
        rules:
          - strategy: full
        """;
        Should.Throw<FormatException>(() => _sut.LoadFromString(yaml));
    }

    [Fact]
    public void LoadFromString_MissingStrategy_ThrowsFormatException()
    {
        const string yaml = """
        rules:
          - fields: [x]
        """;
        Should.Throw<FormatException>(() => _sut.LoadFromString(yaml));
    }
}
