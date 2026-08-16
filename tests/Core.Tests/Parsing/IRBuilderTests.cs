using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Core.Parsing;
using OpenApiDtoGenerator.Core.Resolving;
using NSubstitute.ExceptionExtensions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Parsing;

/// <summary>
/// TDD: Шаг 1.9 — IRBuilder
///
/// Проверяем построение IR-графа из распарсенного документа:
///   - простые типы (string, integer, object)
///   - enum
///   - свойства объекта с required
///   - валидационные правила (minLength, maxLength, pattern, etc.)
///   - массивы
///   - allOf / oneOf / anyOf
///   - discriminator
///   - рекурсивные $ref'ы (lazy refs на циклах)
/// </summary>
public class IRBuilderTests
{
    private static Dictionary<string, object?> BuildTestDocument() => new()
    {
        ["components"] = new Dictionary<string, object?>
        {
            ["schemas"] = new Dictionary<string, object?>
            {
                ["PersonInfo"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "Персональные данные",
                    ["required"] = new List<object?> { "lastName", "firstName" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["lastName"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["maxLength"] = 100,
                            ["pattern"] = "^[А-ЯЁа-яёA-Za-z\\-\\s]+$"
                        },
                        ["firstName"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["maxLength"] = 100
                        },
                        ["middleName"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["maxLength"] = 100
                        }
                    }
                },

                ["DocumentType"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "Тип документа",
                    ["enum"] = new List<object?> { "PASSPORT", "FOREIGN_PASSPORT", "BIRTH_CERTIFICATE" }
                },

                ["PassportInfo"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["required"] = new List<object?> { "series", "number" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["series"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["minLength"] = 4,
                            ["maxLength"] = 4,
                            ["pattern"] = "^\\d{4}$"
                        },
                        ["number"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["minLength"] = 6,
                            ["maxLength"] = 6,
                            ["pattern"] = "^\\d{6}$"
                        }
                    }
                },

                ["Tags"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string"
                    }
                },

                ["INN"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["pattern"] = "^\\d{12}$",
                    ["minLength"] = 12,
                    ["maxLength"] = 12
                },

                ["TreeNode"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["id"] = new Dictionary<string, object?> { ["type"] = "integer" },
                        ["children"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/TreeNode" }
                        }
                    }
                }
            }
        }
    };

    private IRBuilder CreateSut(IRefResolver? refResolver = null)
    {
        refResolver ??= Substitute.For<IRefResolver>();
        return new IRBuilder(refResolver);
    }

    // ═══════════════════════════════════════════════════════════════
    // BASIC SCHEMA BUILDING
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_SimpleSchema_CreatesSchemaNode()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        result.ShouldNotBeEmpty();
        result.ShouldContainKey("PersonInfo");
    }

    [Fact]
    public async Task BuildAsync_PersonInfoSchema_CorrectType()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        result["PersonInfo"].Type.ShouldBe("object");
    }

    [Fact]
    public async Task BuildAsync_PersonInfoSchema_CorrectDescription()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        result["PersonInfo"].Description.ShouldBe("Персональные данные");
    }

    // ═══════════════════════════════════════════════════════════════
    // PROPERTIES & REQUIRED
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_ObjectWithProperties_Extracts()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        var personInfo = result["PersonInfo"];
        personInfo.Properties.Count.ShouldBe(3);
    }

    [Fact]
    public async Task BuildAsync_ObjectWithRequired_MarksCorrectly()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        var personInfo = result["PersonInfo"];
        personInfo.Properties
            .Single(p => p.Name == "lastName").IsRequired.ShouldBeTrue();
        personInfo.Properties
            .Single(p => p.Name == "firstName").IsRequired.ShouldBeTrue();
        personInfo.Properties
            .Single(p => p.Name == "middleName").IsRequired.ShouldBeFalse();
    }

    [Fact]
    public async Task BuildAsync_PropertyWithValidationRules_Extracts()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        var lastName = result["PersonInfo"].Properties.Single(p => p.Name == "lastName");
        lastName.Schema.Rules.MaxLength.ShouldBe(100);
        lastName.Schema.Rules.Pattern.ShouldBe("^[А-ЯЁа-яёA-Za-z\\-\\s]+$");
    }

    // ═══════════════════════════════════════════════════════════════
    // ENUM
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_EnumSchema_Extracts()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        var docType = result["DocumentType"];
        docType.IsEnum.ShouldBeTrue();
        docType.EnumValues.ShouldBe(new[] { "PASSPORT", "FOREIGN_PASSPORT", "BIRTH_CERTIFICATE" });
    }

    // ═══════════════════════════════════════════════════════════════
    // ARRAY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_ArraySchema_Extracts()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        var tags = result["Tags"];
        tags.IsArray.ShouldBeTrue();
        tags.Items.ShouldNotBeNull();
        tags.Items!.Type.ShouldBe("string");
    }

    // ═══════════════════════════════════════════════════════════════
    // VALIDATION RULES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_StringWithLengthRules_Extracts()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        var inn = result["INN"];
        inn.Rules.MinLength.ShouldBe(12);
        inn.Rules.MaxLength.ShouldBe(12);
        inn.Rules.Pattern.ShouldBe("^\\d{12}$");
    }

    [Fact]
    public async Task BuildAsync_PassportWithMultipleRules_AllExtracted()
    {
        var doc = BuildTestDocument();
        var sut = CreateSut();

        var result = await sut.BuildAsync(doc, "/spec.yaml");

        var series = result["PassportInfo"].Properties.Single(p => p.Name == "series");
        series.Schema.Rules.MinLength.ShouldBe(4);
        series.Schema.Rules.MaxLength.ShouldBe(4);
        series.Schema.Rules.Pattern.ShouldBe("^\\d{4}$");
    }

    // ═══════════════════════════════════════════════════════════════
    // CYCLE DETECTION & LAZY REF
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_RecursiveSchema_HandlesGracefully()
    {
        // TreeNode → children → array items → TreeNode (цикл)
        // Без RefResolver это должно отработать без ошибки, используя lazy ref
        var refResolver = Substitute.For<IRefResolver>();
        refResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new CyclicReferenceException("#/components/schemas/TreeNode", new[] { "TreeNode" }));

        var doc = BuildTestDocument();
        var sut = new IRBuilder(refResolver);

        // Не должно бросить исключение
        var result = await sut.BuildAsync(doc, "/spec.yaml");

        result.ShouldContainKey("TreeNode");
        var treeNode = result["TreeNode"];
        treeNode.IsObject.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // EMPTY DOCUMENT
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_NoSchemas_ReturnsEmpty()
    {
        var doc = new Dictionary<string, object?>
        {
            ["openapi"] = "3.0.3",
            ["info"] = new Dictionary<string, object?> { ["title"] = "Test" }
        };

        var sut = CreateSut();
        var result = await sut.BuildAsync(doc, "/spec.yaml");

        result.ShouldBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARD
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildAsync_NullDocumentTree_ThrowsArgumentException()
    {
        var sut = CreateSut();
        await Should.ThrowAsync<ArgumentException>(() => sut.BuildAsync(null!, "/spec.yaml"));
    }

    [Fact]
    public async Task BuildAsync_NotDictionaryTree_ThrowsArgumentException()
    {
        var sut = CreateSut();
        await Should.ThrowAsync<ArgumentException>(() => sut.BuildAsync("not a dict", "/spec.yaml"));
    }

    [Fact]
    public async Task BuildAsync_NullRefResolver_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new IRBuilder(null!));
    }
}
