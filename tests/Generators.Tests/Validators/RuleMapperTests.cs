using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Generators.Validators;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.Validators;

/// <summary>
/// TDD: RuleMapper — маппинг ValidationRules → строки FluentValidation вызовов.
///
/// Каждый тест проверяет одно правило изолированно.
/// </summary>
public class RuleMapperTests
{
    private readonly RuleMapper _sut = new();

    // ═══════════════════════════════════════════════════════════════
    // REQUIRED / NOT EMPTY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_RequiredStringProp_GeneratesNotEmpty()
    {
        var prop = MakeProp("lastName", "string", isRequired: true);
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("NotEmpty()"));
    }

    [Fact]
    public void Map_RequiredObjectProp_GeneratesNotNull()
    {
        var prop = MakeProp("personInfo", "object", isRequired: true);
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("NotNull()"));
    }

    [Fact]
    public void Map_OptionalProp_NoNotEmptyRule()
    {
        var prop = MakeProp("middleName", "string", isRequired: false);
        var rules = _sut.Map(prop);
        rules.ShouldNotContain(r => r.Contains("NotEmpty()"));
    }

    // ═══════════════════════════════════════════════════════════════
    // MIN / MAX LENGTH
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_MinLength_GeneratesMinimumLength()
    {
        var prop = MakeProp("series", "string", rules: new ValidationRules { MinLength = 4 });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("MinimumLength(4)"));
    }

    [Fact]
    public void Map_MaxLength_GeneratesMaximumLength()
    {
        var prop = MakeProp("series", "string", rules: new ValidationRules { MaxLength = 4 });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("MaximumLength(4)"));
    }

    [Fact]
    public void Map_MinAndMaxLength_GeneratesBoth()
    {
        var prop = MakeProp("series", "string", rules: new ValidationRules { MinLength = 4, MaxLength = 4 });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("MinimumLength(4)"));
        rules.ShouldContain(r => r.Contains("MaximumLength(4)"));
    }

    // ═══════════════════════════════════════════════════════════════
    // PATTERN
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_Pattern_GeneratesMatches()
    {
        var prop = MakeProp("series", "string",
            rules: new ValidationRules { Pattern = @"^\d{4}$" });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("Matches(") && r.Contains(@"\d{4}"));
    }

    [Fact]
    public void Map_PatternWithSpecialChars_EscapedCorrectly()
    {
        var prop = MakeProp("inn", "string",
            rules: new ValidationRules { Pattern = @"^\d{12}$" });
        var rules = _sut.Map(prop);
        // Паттерн должен быть обёрнут в verbatim-строку @"..."
        rules.ShouldContain(r => r.Contains(@"@""^\d{12}$""") || r.Contains(@"\d{12}"));
    }

    // ═══════════════════════════════════════════════════════════════
    // NUMERIC RANGE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_Minimum_GeneratesGreaterThanOrEqualTo()
    {
        var prop = MakeProp("age", "integer",
            rules: new ValidationRules { Minimum = 0 });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("GreaterThanOrEqualTo(0)"));
    }

    [Fact]
    public void Map_Maximum_GeneratesLessThanOrEqualTo()
    {
        var prop = MakeProp("age", "integer",
            rules: new ValidationRules { Maximum = 150 });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("LessThanOrEqualTo(150)"));
    }

    [Fact]
    public void Map_ExclusiveMinimum_GeneratesGreaterThan()
    {
        var prop = MakeProp("value", "number",
            rules: new ValidationRules { Minimum = 0, ExclusiveMinimum = true });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("GreaterThan(0)"));
    }

    [Fact]
    public void Map_ExclusiveMaximum_GeneratesLessThan()
    {
        var prop = MakeProp("value", "number",
            rules: new ValidationRules { Maximum = 100, ExclusiveMaximum = true });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("LessThan(100)"));
    }

    // ═══════════════════════════════════════════════════════════════
    // FORMAT
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_EmailFormat_GeneratesEmailAddress()
    {
        var prop = MakeProp("email", "string",
            rules: new ValidationRules { Format = "email" });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("EmailAddress()"));
    }

    [Fact]
    public void Map_UuidFormat_GeneratesGuidValidation()
    {
        var prop = MakeProp("id", "string",
            rules: new ValidationRules { Format = "uuid" });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("Guid.TryParse") || r.Contains("Must("));
    }

    [Fact]
    public void Map_DateFormat_GeneratesDateValidation()
    {
        var prop = MakeProp("birthDate", "string",
            rules: new ValidationRules { Format = "date" });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("DateOnly.TryParse") || r.Contains("Must("));
    }

    // ═══════════════════════════════════════════════════════════════
    // ARRAY ITEMS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_MinItems_GeneratesCollectionRule()
    {
        var prop = MakeProp("tags", "array",
            rules: new ValidationRules { MinItems = 1 });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("1") && (r.Contains("Count") || r.Contains("MinimumCount")));
    }

    [Fact]
    public void Map_MaxItems_GeneratesCollectionRule()
    {
        var prop = MakeProp("tags", "array",
            rules: new ValidationRules { MaxItems = 10 });
        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("10") && (r.Contains("Count") || r.Contains("MaximumCount")));
    }

    // ═══════════════════════════════════════════════════════════════
    // ENUM
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_EnumProperty_GeneratesIsInEnumRule()
    {
        var schema = new SchemaNode
        {
            Name = "DocumentType",
            Type = "string",
            EnumValues = new[] { "PASSPORT", "FOREIGN_PASSPORT" }
        };
        var prop = new PropertyNode
        {
            Name = "documentType",
            Schema = schema,
            IsRequired = true
        };

        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("IsInEnum()") || r.Contains("Enum.IsDefined"));
    }

    // ═══════════════════════════════════════════════════════════════
    // NO RULES — пустой список
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_NoRules_ReturnsEmptyList()
    {
        var prop = MakeProp("someField", "string",
            isRequired: false,
            rules: ValidationRules.Empty);
        var rules = _sut.Map(prop);
        rules.ShouldBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // WITH MESSAGE — description становится сообщением об ошибке
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_PropWithDescription_AddsWithMessage()
    {
        var prop = new PropertyNode
        {
            Name = "series",
            Description = "Серия паспорта (4 цифры)",
            Schema = new SchemaNode
            {
                Type = "string",
                Rules = new ValidationRules { MinLength = 4 }
            },
            IsRequired = false
        };

        var rules = _sut.Map(prop);
        rules.ShouldContain(r => r.Contains("WithMessage") && r.Contains("Серия паспорта"));
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARD
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_NullProp_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => _sut.Map(null!));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static PropertyNode MakeProp(
        string name,
        string type,
        bool isRequired = false,
        ValidationRules? rules = null,
        string? description = null)
        => new()
        {
            Name = name,
            Description = description,
            IsRequired = isRequired,
            Schema = new SchemaNode
            {
                Type = type,
                Rules = rules ?? ValidationRules.Empty
            }
        };
}
