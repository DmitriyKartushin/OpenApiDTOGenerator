using OpenApiDtoGenerator.Generators.Masking;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.Masking;

/// <summary>
/// TDD: MaskingConfig — модели конфигурации маскировки полей.
///
/// Конфиг задаёт какие поля маскировать и как:
///   - Full     "***"
///   - Partial  видны первые N + последние M символов
///   - FirstChar "И*****"
///   - YearOnly дата → только год
///   - Hash    хэш начала строки
/// </summary>
public class MaskingConfigTests
{
    [Fact]
    public void MaskingRule_Full_HasNoParameters()
    {
        var rule = new MaskingRule(
            Fields: new[] { "issuedBy" },
            Strategy: MaskStrategy.Full);

        rule.Strategy.ShouldBe(MaskStrategy.Full);
        rule.Fields.ShouldContain("issuedBy");
        rule.VisibleStart.ShouldBe(0);
        rule.VisibleEnd.ShouldBe(0);
    }

    [Fact]
    public void MaskingRule_Partial_KeepsVisibleCounts()
    {
        var rule = new MaskingRule(
            Fields: new[] { "series", "number" },
            Strategy: MaskStrategy.Partial,
            VisibleStart: 2,
            VisibleEnd: 2);

        rule.Strategy.ShouldBe(MaskStrategy.Partial);
        rule.VisibleStart.ShouldBe(2);
        rule.VisibleEnd.ShouldBe(2);
        rule.Fields.Count.ShouldBe(2);
    }

    [Fact]
    public void MaskingConfig_FindRuleForField_ReturnsMatchingRule()
    {
        var config = new MaskingConfig(new[]
        {
            new MaskingRule(new[] { "series", "number" }, MaskStrategy.Partial, VisibleStart: 2, VisibleEnd: 2),
            new MaskingRule(new[] { "lastName" }, MaskStrategy.FirstChar)
        });

        var seriesRule = config.FindRuleForField("series");
        seriesRule.ShouldNotBeNull();
        seriesRule!.Strategy.ShouldBe(MaskStrategy.Partial);

        var lastNameRule = config.FindRuleForField("lastName");
        lastNameRule.ShouldNotBeNull();
        lastNameRule!.Strategy.ShouldBe(MaskStrategy.FirstChar);
    }

    [Fact]
    public void MaskingConfig_FindRuleForField_ReturnsNullForUnknownField()
    {
        var config = new MaskingConfig(new[]
        {
            new MaskingRule(new[] { "series" }, MaskStrategy.Partial)
        });

        config.FindRuleForField("unknownField").ShouldBeNull();
    }

    [Fact]
    public void MaskingConfig_Empty_FindReturnsNull()
    {
        var config = new MaskingConfig(Array.Empty<MaskingRule>());
        config.FindRuleForField("anything").ShouldBeNull();
    }

    [Fact]
    public void MaskingConfig_CaseInsensitiveMatch_FindsField()
    {
        // Соответствие имён должно быть case-insensitive
        var config = new MaskingConfig(new[]
        {
            new MaskingRule(new[] { "Series" }, MaskStrategy.Partial)
        });

        config.FindRuleForField("series").ShouldNotBeNull();
        config.FindRuleForField("SERIES").ShouldNotBeNull();
    }
}
