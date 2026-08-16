using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Generators.Validators;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.Validators;

/// <summary>
/// TDD: ValidatorGenerator — генерация AbstractValidator&lt;T&gt; из SchemaNode.
/// </summary>
public class ValidatorGeneratorTests
{
    private static DtoValidatorOptions DefaultOptions => new(Namespace: "MyApp.Validators");
    private ValidatorGenerator CreateSut() => new();

    // ═══════════════════════════════════════════════════════════════
    // БАЗОВАЯ СТРУКТУРА
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_SimpleSchema_ContainsNamespace()
    {
        var schema = SimplePersonSchema();
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("namespace MyApp.Validators");
    }

    [Fact]
    public void Generate_SimpleSchema_ContainsValidatorClass()
    {
        var schema = SimplePersonSchema();
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("public class PersonInfoValidator");
        result.Content.ShouldContain(": AbstractValidator<PersonInfo>");
    }

    [Fact]
    public void Generate_SimpleSchema_FileName()
    {
        var schema = SimplePersonSchema();
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.FileName.ShouldBe("PersonInfoValidator.cs");
    }

    [Fact]
    public void Generate_SimpleSchema_ContainsConstructor()
    {
        var schema = SimplePersonSchema();
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("public PersonInfoValidator()");
    }

    [Fact]
    public void Generate_SimpleSchema_ContainsUsings()
    {
        var schema = SimplePersonSchema();
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("using FluentValidation");
    }

    // ═══════════════════════════════════════════════════════════════
    // ПРАВИЛА ДЛЯ СВОЙСТВ
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_RequiredStringProp_GeneratesRuleFor()
    {
        var schema = SimplePersonSchema();
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("RuleFor(x => x.LastName)");
    }

    [Fact]
    public void Generate_RequiredProp_ContainsNotEmpty()
    {
        var schema = SimplePersonSchema();
        var result = CreateSut().Generate(schema, DefaultOptions);
        // lastName — required string
        result.Content.ShouldContain("NotEmpty()");
    }

    [Fact]
    public void Generate_PropWithMaxLength_ContainsMaximumLength()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new()
                {
                    Name = "lastName",
                    IsRequired = true,
                    Schema = new SchemaNode
                    {
                        Type = "string",
                        Rules = new ValidationRules { MinLength = 1, MaxLength = 100 }
                    }
                }
            }
        };
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("MinimumLength(1)");
        result.Content.ShouldContain("MaximumLength(100)");
    }

    [Fact]
    public void Generate_PropWithPattern_ContainsMatches()
    {
        var schema = new SchemaNode
        {
            Name = "PassportInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new()
                {
                    Name = "series",
                    IsRequired = true,
                    Schema = new SchemaNode
                    {
                        Type = "string",
                        Rules = new ValidationRules { Pattern = @"^\d{4}$" }
                    }
                }
            }
        };
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("Matches(");
        result.Content.ShouldContain(@"\d{4}");
    }

    // ═══════════════════════════════════════════════════════════════
    // КАСКАДНАЯ ВАЛИДАЦИЯ (вложенные объекты)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_NestedObjectProp_GeneratesSetValidator()
    {
        var schema = new SchemaNode
        {
            Name = "CreatePersonRequestBody",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new()
                {
                    Name = "personInfo",
                    IsRequired = true,
                    Schema = new SchemaNode { Name = "PersonInfo", Type = "object" }
                }
            }
        };
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("SetValidator(new PersonInfoValidator())");
    }

    [Fact]
    public void Generate_NestedObjectProp_AlsoHasNotNull()
    {
        var schema = new SchemaNode
        {
            Name = "CreatePersonRequestBody",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new()
                {
                    Name = "personInfo",
                    IsRequired = true,
                    Schema = new SchemaNode { Name = "PersonInfo", Type = "object" }
                }
            }
        };
        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain("NotNull()");
    }

    // ═══════════════════════════════════════════════════════════════
    // УСЛОВНАЯ ВАЛИДАЦИЯ (discriminator / When)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_DiscriminatorSchema_GeneratesWhen()
    {
        var schema = new SchemaNode
        {
            Name = "IdentityDocument",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new()
                {
                    Name = "documentType",
                    IsRequired = true,
                    Schema = new SchemaNode
                    {
                        Name = "DocumentType",
                        Type = "string",
                        EnumValues = new[] { "PASSPORT", "FOREIGN_PASSPORT" }
                    }
                },
                new()
                {
                    Name = "passport",
                    IsRequired = false,
                    Schema = new SchemaNode { Name = "PassportInfo", Type = "object" }
                },
                new()
                {
                    Name = "foreignPassport",
                    IsRequired = false,
                    Schema = new SchemaNode { Name = "ForeignPassportInfo", Type = "object" }
                }
            },
            DiscriminatorPropertyName = "documentType",
            DiscriminatorMapping = new Dictionary<string, string>
            {
                ["PASSPORT"] = "PassportInfo",
                ["FOREIGN_PASSPORT"] = "ForeignPassportInfo"
            }
        };

        var result = CreateSut().Generate(schema, DefaultOptions);
        result.Content.ShouldContain(".When(");
        result.Content.ShouldContain("DocumentType");
    }

    // ═══════════════════════════════════════════════════════════════
    // PREFIX / SUFFIX
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_WithSuffix_AppliesInClassName()
    {
        var schema = SimplePersonSchema();
        var options = new DtoValidatorOptions(Namespace: "MyApp.Validators", ClassSuffix: "V2");
        var result = CreateSut().Generate(schema, options);
        result.Content.ShouldContain("PersonInfoV2Validator");
    }

    // ═══════════════════════════════════════════════════════════════
    // GenerateAll
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateAll_MultipleSchemas_AllValidatorsPresent()
    {
        var schemas = new Dictionary<string, SchemaNode>
        {
            ["PersonInfo"] = SimplePersonSchema(),
            ["PassportInfo"] = new SchemaNode
            {
                Name = "PassportInfo", Type = "object",
                Properties = new List<PropertyNode>
                {
                    new() { Name = "series", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
                }
            }
        };

        var results = CreateSut().GenerateAll(schemas, DefaultOptions);

        results.Count.ShouldBe(2);
        results.ShouldContain(f => f.FileName == "PersonInfoValidator.cs");
        results.ShouldContain(f => f.FileName == "PassportInfoValidator.cs");
    }

    [Fact]
    public void GenerateAll_EnumSchema_Skipped()
    {
        // Enum-схемы не нуждаются в отдельном валидаторе
        var schemas = new Dictionary<string, SchemaNode>
        {
            ["DocumentType"] = new SchemaNode
            {
                Name = "DocumentType", Type = "string",
                EnumValues = new[] { "PASSPORT" }
            }
        };

        var results = CreateSut().GenerateAll(schemas, DefaultOptions);
        results.ShouldBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARD
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_NullSchema_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => CreateSut().Generate(null!, DefaultOptions));
    }

    [Fact]
    public void Generate_SchemaWithoutName_ThrowsInvalidOperationException()
    {
        var schema = new SchemaNode { Type = "object", Properties = [] };
        Should.Throw<InvalidOperationException>(() => CreateSut().Generate(schema, DefaultOptions));
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static SchemaNode SimplePersonSchema() => new()
    {
        Name = "PersonInfo",
        Type = "object",
        Properties = new List<PropertyNode>
        {
            new()
            {
                Name = "lastName",
                IsRequired = true,
                Description = "Фамилия",
                Schema = new SchemaNode
                {
                    Type = "string",
                    Rules = new ValidationRules { MinLength = 1, MaxLength = 100 }
                }
            },
            new()
            {
                Name = "middleName",
                IsRequired = false,
                Schema = new SchemaNode { Type = "string", Rules = new ValidationRules { MaxLength = 100 } }
            }
        }
    };
}
