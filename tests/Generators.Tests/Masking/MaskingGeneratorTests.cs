using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Generators.Masking;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.Masking;

/// <summary>
/// TDD: MaskingGenerator — генерирует:
///   1. Runtime-инфраструктуру (MaskStrategy enum, MaskAttribute, MaskingJsonConverter)
///   2. Дополняет DTO-классы атрибутами [Mask(...)] и методом ToMaskedJson()
/// </summary>
public class MaskingGeneratorTests
{
    private static MaskingGeneratorOptions DefaultOptions =>
        new(Namespace: "MyApp.Masking",
            DtoNamespace: "MyApp.Dto",
            Config: new MaskingConfig(new[]
            {
                new MaskingRule(new[] { "series", "number" }, MaskStrategy.Partial, VisibleStart: 2, VisibleEnd: 2),
                new MaskingRule(new[] { "inn" }, MaskStrategy.Partial, VisibleStart: 3, VisibleEnd: 2),
                new MaskingRule(new[] { "lastName", "firstName", "middleName" }, MaskStrategy.FirstChar),
                new MaskingRule(new[] { "issuedBy" }, MaskStrategy.Full),
                new MaskingRule(new[] { "birthDate" }, MaskStrategy.YearOnly)
            }));

    private MaskingGenerator CreateSut() => new();

    // ═══════════════════════════════════════════════════════════════
    // INFRASTRUCTURE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateInfrastructure_ContainsMaskStrategyEnum()
    {
        var files = CreateSut().GenerateInfrastructure(DefaultOptions);
        var enumFile = files.SingleOrDefault(f => f.FileName == "MaskStrategy.cs");

        enumFile.ShouldNotBeNull();
        enumFile!.Content.ShouldContain("public enum MaskStrategy");
        enumFile.Content.ShouldContain("Full");
        enumFile.Content.ShouldContain("Partial");
        enumFile.Content.ShouldContain("FirstChar");
        enumFile.Content.ShouldContain("YearOnly");
        enumFile.Content.ShouldContain("Hash");
        enumFile.Content.ShouldContain("None");
    }

    [Fact]
    public void GenerateInfrastructure_ContainsMaskAttribute()
    {
        var files = CreateSut().GenerateInfrastructure(DefaultOptions);
        var attrFile = files.SingleOrDefault(f => f.FileName == "MaskAttribute.cs");

        attrFile.ShouldNotBeNull();
        attrFile!.Content.ShouldContain("public sealed class MaskAttribute : Attribute");
        attrFile.Content.ShouldContain("AttributeTargets.Property");
        attrFile.Content.ShouldContain("MaskStrategy Strategy");
    }

    [Fact]
    public void GenerateInfrastructure_ContainsStrategyExecutor()
    {
        var files = CreateSut().GenerateInfrastructure(DefaultOptions);
        var executorFile = files.SingleOrDefault(f => f.FileName == "MaskingStrategyExecutor.cs");

        executorFile.ShouldNotBeNull();
        executorFile!.Content.ShouldContain("public string? Apply(");
        executorFile.Content.ShouldContain("MaskStrategy.Full");
        executorFile.Content.ShouldContain("MaskStrategy.Partial");
    }

    [Fact]
    public void GenerateInfrastructure_ContainsJsonConverter()
    {
        var files = CreateSut().GenerateInfrastructure(DefaultOptions);
        var converterFile = files.SingleOrDefault(f => f.FileName == "MaskingJsonConverter.cs");

        converterFile.ShouldNotBeNull();
        converterFile!.Content.ShouldContain("JsonConverter");
    }

    [Fact]
    public void GenerateInfrastructure_ContainsMaskingJsonOptions()
    {
        var files = CreateSut().GenerateInfrastructure(DefaultOptions);
        var optsFile = files.SingleOrDefault(f => f.FileName == "MaskingJsonOptions.cs");

        optsFile.ShouldNotBeNull();
        optsFile!.Content.ShouldContain("public static class MaskingJsonOptions");
        optsFile.Content.ShouldContain("public static JsonSerializerOptions Default");
    }

    [Fact]
    public void GenerateInfrastructure_UsesProvidedNamespace()
    {
        var files = CreateSut().GenerateInfrastructure(DefaultOptions);
        files.ShouldAllBe(f => f.Content.Contains("namespace MyApp.Masking"));
    }

    // ═══════════════════════════════════════════════════════════════
    // DTO EXTENSIONS — partial class + ToMaskedJson()
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateDtoExtension_CreatesPartialClass()
    {
        var schema = new SchemaNode
        {
            Name = "PassportInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "series", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
            }
        };

        var file = CreateSut().GenerateDtoExtension(schema, DefaultOptions);
        file.ShouldNotBeNull();
        file!.Content.ShouldContain("public partial class PassportInfo");
        file.Content.ShouldContain("ToMaskedJson()");
    }

    [Fact]
    public void GenerateDtoExtension_FileName()
    {
        var schema = new SchemaNode
        {
            Name = "PassportInfo", Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "series", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
            }
        };

        var file = CreateSut().GenerateDtoExtension(schema, DefaultOptions);
        file!.FileName.ShouldBe("PassportInfo.Masking.cs");
    }

    [Fact]
    public void GenerateDtoExtension_UsesDtoNamespace()
    {
        var schema = new SchemaNode
        {
            Name = "PassportInfo", Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "series", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
            }
        };

        var file = CreateSut().GenerateDtoExtension(schema, DefaultOptions);
        // partial-класс должен быть в том же namespace, что и оригинальный DTO
        file!.Content.ShouldContain("namespace MyApp.Dto");
    }

    [Fact]
    public void GenerateDtoExtension_ContainsToMaskedJsonMethod()
    {
        var schema = new SchemaNode
        {
            Name = "PassportInfo", Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "series", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
            }
        };

        var file = CreateSut().GenerateDtoExtension(schema, DefaultOptions);
        file!.Content.ShouldContain("public string ToMaskedJson()");
        file.Content.ShouldContain("JsonSerializer.Serialize");
        file.Content.ShouldContain("MaskingJsonOptions.Default");
    }

    [Fact]
    public void GenerateDtoExtension_SchemaWithoutMaskedFields_ReturnsNull()
    {
        // Если ни одно поле не подпадает под маскировку — не генерируем расширение
        var schema = new SchemaNode
        {
            Name = "SimpleSchema",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "unmaskedField", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
            }
        };

        var file = CreateSut().GenerateDtoExtension(schema, DefaultOptions);
        file.ShouldBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // MASK ATTRIBUTES ON DTO PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetMaskAttribute_PartialField_ReturnsAttributeString()
    {
        var sut = CreateSut();
        var attr = sut.GetMaskAttributeForProperty("series", DefaultOptions.Config);

        attr.ShouldNotBeNull();
        attr.ShouldContain("Mask");
        attr.ShouldContain("Partial");
        attr.ShouldContain("2");
    }

    [Fact]
    public void GetMaskAttribute_FullField_ReturnsAttributeString()
    {
        var sut = CreateSut();
        var attr = sut.GetMaskAttributeForProperty("issuedBy", DefaultOptions.Config);

        attr.ShouldNotBeNull();
        attr.ShouldContain("MaskStrategy.Full");
    }

    [Fact]
    public void GetMaskAttribute_FirstCharField_ReturnsAttributeString()
    {
        var sut = CreateSut();
        var attr = sut.GetMaskAttributeForProperty("lastName", DefaultOptions.Config);

        attr.ShouldNotBeNull();
        attr.ShouldContain("MaskStrategy.FirstChar");
    }

    [Fact]
    public void GetMaskAttribute_UnknownField_ReturnsNull()
    {
        var sut = CreateSut();
        var attr = sut.GetMaskAttributeForProperty("unknownField", DefaultOptions.Config);

        attr.ShouldBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // GenerateAll
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateAll_ProducesInfrastructureAndExtensions()
    {
        var schemas = new Dictionary<string, SchemaNode>
        {
            ["PassportInfo"] = new SchemaNode
            {
                Name = "PassportInfo", Type = "object",
                Properties = new List<PropertyNode>
                {
                    new() { Name = "series", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
                }
            },
            ["PersonInfo"] = new SchemaNode
            {
                Name = "PersonInfo", Type = "object",
                Properties = new List<PropertyNode>
                {
                    new() { Name = "lastName", IsRequired = true, Schema = new SchemaNode { Type = "string" } }
                }
            }
        };

        var files = CreateSut().GenerateAll(schemas, DefaultOptions);

        // Инфраструктура (5 файлов) + 2 extension'а
        files.Count.ShouldBeGreaterThanOrEqualTo(6);
        files.ShouldContain(f => f.FileName == "MaskStrategy.cs");
        files.ShouldContain(f => f.FileName == "PassportInfo.Masking.cs");
        files.ShouldContain(f => f.FileName == "PersonInfo.Masking.cs");
    }
}
