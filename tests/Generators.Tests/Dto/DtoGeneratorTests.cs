using OpenApiDtoGenerator.Generators.Dto;
using OpenApiDtoGenerator.Core.Models;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.Dto;

/// <summary>
/// TDD: DtoGenerator — генерация C# классов/enum'ов из SchemaNode.
///
/// Проверяем текстовое содержимое сгенерированного кода:
///   - namespace, using
///   - XML doc comментарии из Description
///   - [JsonPropertyName] атрибуты
///   - required vs nullable свойства
///   - enum генерация
///   - allOf → наследование
/// </summary>
public class DtoGeneratorTests
{
    private static DtoGeneratorOptions DefaultOptions => new(
        Namespace: "MyApp.Dto",
        ClassPrefix: "",
        ClassSuffix: "");

    private DtoGenerator CreateSut() => new();

    // ═══════════════════════════════════════════════════════════════
    // SIMPLE CLASS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateClass_SimpleSchema_ContainsNamespace()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "firstName", Schema = new SchemaNode { Type = "string" }, IsRequired = true }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("namespace MyApp.Dto");
    }

    [Fact]
    public void GenerateClass_SimpleSchema_ContainsClassDeclaration()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "firstName", Schema = new SchemaNode { Type = "string" }, IsRequired = true }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("public class PersonInfo");
    }

    [Fact]
    public void GenerateClass_WithDescription_ContainsXmlDoc()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Description = "Персональные данные физического лица",
            Properties = []
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("/// <summary>");
        result.Content.ShouldContain("Персональные данные физического лица");
    }

    [Fact]
    public void GenerateClass_RequiredStringProperty_NotNullable()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "lastName", Schema = new SchemaNode { Type = "string" }, IsRequired = true }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("public string LastName { get; set; }");
    }

    [Fact]
    public void GenerateClass_OptionalStringProperty_IsNullable()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "middleName", Schema = new SchemaNode { Type = "string" }, IsRequired = false }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("public string? MiddleName { get; set; }");
    }

    [Fact]
    public void GenerateClass_PropertyName_ConvertedToPascalCase()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "firstName", Schema = new SchemaNode { Type = "string" }, IsRequired = true }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        // camelCase yaml -> PascalCase C#
        result.Content.ShouldContain("FirstName");
    }

    [Fact]
    public void GenerateClass_Property_HasJsonPropertyNameAttribute()
    {
        var schema = new SchemaNode
        {
            Name = "PersonInfo",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new() { Name = "firstName", Schema = new SchemaNode { Type = "string" }, IsRequired = true }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("""[JsonPropertyName("firstName")]""");
    }

    [Fact]
    public void GenerateClass_NestedObjectProperty_UsesClassName()
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
                    Schema = new SchemaNode { Type = "object", Name = "PersonInfo" },
                    IsRequired = true
                }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("public PersonInfo PersonInfo { get; set; }");
    }

    [Fact]
    public void GenerateClass_ArrayProperty_UsesListType()
    {
        var schema = new SchemaNode
        {
            Name = "Container",
            Type = "object",
            Properties = new List<PropertyNode>
            {
                new()
                {
                    Name = "tags",
                    Schema = new SchemaNode { Type = "array", Items = new SchemaNode { Type = "string" } },
                    IsRequired = false
                }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("List<string>");
    }

    [Fact]
    public void GenerateClass_WithPrefixAndSuffix_AppliesBoth()
    {
        var schema = new SchemaNode { Name = "PersonInfo", Type = "object", Properties = [] };
        var options = new DtoGeneratorOptions(Namespace: "MyApp.Dto", ClassPrefix: "Dto", ClassSuffix: "Model");

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, options);

        result.Content.ShouldContain("public class DtoPersonInfoModel");
    }

    [Fact]
    public void GenerateClass_FileName_MatchesClassName()
    {
        var schema = new SchemaNode { Name = "PersonInfo", Type = "object", Properties = [] };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.FileName.ShouldBe("PersonInfo.cs");
    }

    // ═══════════════════════════════════════════════════════════════
    // ENUM
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateEnum_SimpleEnum_ContainsEnumDeclaration()
    {
        var schema = new SchemaNode
        {
            Name = "DocumentType",
            Type = "string",
            EnumValues = new[] { "PASSPORT", "FOREIGN_PASSPORT" }
        };

        var sut = CreateSut();
        var result = sut.GenerateEnum(schema, DefaultOptions);

        result.Content.ShouldContain("public enum DocumentType");
    }

    [Fact]
    public void GenerateEnum_ContainsAllValues()
    {
        var schema = new SchemaNode
        {
            Name = "DocumentType",
            Type = "string",
            EnumValues = new[] { "PASSPORT", "FOREIGN_PASSPORT", "BIRTH_CERTIFICATE" }
        };

        var sut = CreateSut();
        var result = sut.GenerateEnum(schema, DefaultOptions);

        result.Content.ShouldContain("PASSPORT");
        result.Content.ShouldContain("FOREIGN_PASSPORT");
        result.Content.ShouldContain("BIRTH_CERTIFICATE");
    }

    [Fact]
    public void GenerateEnum_ValuesHaveEnumMemberAttribute()
    {
        var schema = new SchemaNode
        {
            Name = "DocumentType",
            Type = "string",
            EnumValues = new[] { "PASSPORT" }
        };

        var sut = CreateSut();
        var result = sut.GenerateEnum(schema, DefaultOptions);

        result.Content.ShouldContain("""[EnumMember(Value = "PASSPORT")]""");
    }

    // ═══════════════════════════════════════════════════════════════
    // allOf → INHERITANCE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateClass_AllOfWithNamedBase_GeneratesInheritance()
    {
        var baseSchema = new SchemaNode { Name = "ErrorResponse", Type = "object" };

        var schema = new SchemaNode
        {
            Name = "ValidationErrorResponse",
            Type = "object",
            AllOf = new List<SchemaNode>
            {
                baseSchema,
                new SchemaNode
                {
                    Type = "object",
                    Properties = new List<PropertyNode>
                    {
                        new() { Name = "violations", Schema = new SchemaNode { Type = "array", Items = new SchemaNode { Type = "string" } } }
                    }
                }
            }
        };

        var sut = CreateSut();
        var result = sut.GenerateClass(schema, DefaultOptions);

        result.Content.ShouldContain("public class ValidationErrorResponse : ErrorResponse");
    }

    // ═══════════════════════════════════════════════════════════════
    // GenerateAll — batch processing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateAll_MixOfObjectsAndEnums_GeneratesAllFiles()
    {
        var schemas = new Dictionary<string, SchemaNode>
        {
            ["PersonInfo"] = new SchemaNode { Name = "PersonInfo", Type = "object", Properties = [] },
            ["DocumentType"] = new SchemaNode { Name = "DocumentType", Type = "string", EnumValues = new[] { "PASSPORT" } }
        };

        var sut = CreateSut();
        var results = sut.GenerateAll(schemas, DefaultOptions);

        results.Count.ShouldBe(2);
        results.ShouldContain(f => f.FileName == "PersonInfo.cs");
        results.ShouldContain(f => f.FileName == "DocumentType.cs");
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARD
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateClass_NullSchema_ThrowsArgumentNullException()
    {
        var sut = CreateSut();
        Should.Throw<ArgumentNullException>(() => sut.GenerateClass(null!, DefaultOptions));
    }

    [Fact]
    public void GenerateClass_SchemaWithoutName_ThrowsInvalidOperationException()
    {
        var schema = new SchemaNode { Type = "object", Properties = [] };
        var sut = CreateSut();

        Should.Throw<InvalidOperationException>(() => sut.GenerateClass(schema, DefaultOptions));
    }
}
