using OpenApiDtoGenerator.Generators.Dto;
using OpenApiDtoGenerator.Core.Models;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.Dto;

/// <summary>
/// TDD: TypeMapper — маппинг OpenAPI типов (type + format) в C# типы.
/// </summary>
public class TypeMapperTests
{
    private readonly TypeMapper _sut = new();

    // ═══════════════════════════════════════════════════════════════
    // STRING
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_PlainString_ReturnsString()
    {
        var node = new SchemaNode { Type = "string" };
        _sut.Map(node).ShouldBe("string");
    }

    [Fact]
    public void Map_StringDateFormat_ReturnsDateOnly()
    {
        var node = new SchemaNode { Type = "string", Rules = new ValidationRules { Format = "date" } };
        _sut.Map(node).ShouldBe("DateOnly");
    }

    [Fact]
    public void Map_StringDateTimeFormat_ReturnsDateTime()
    {
        var node = new SchemaNode { Type = "string", Rules = new ValidationRules { Format = "date-time" } };
        _sut.Map(node).ShouldBe("DateTime");
    }

    [Fact]
    public void Map_StringUuidFormat_ReturnsGuid()
    {
        var node = new SchemaNode { Type = "string", Rules = new ValidationRules { Format = "uuid" } };
        _sut.Map(node).ShouldBe("Guid");
    }

    [Fact]
    public void Map_StringByteFormat_ReturnsByteArray()
    {
        var node = new SchemaNode { Type = "string", Rules = new ValidationRules { Format = "byte" } };
        _sut.Map(node).ShouldBe("byte[]");
    }

    // ═══════════════════════════════════════════════════════════════
    // NUMERIC
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_IntegerNoFormat_ReturnsInt()
    {
        var node = new SchemaNode { Type = "integer" };
        _sut.Map(node).ShouldBe("int");
    }

    [Fact]
    public void Map_IntegerInt32Format_ReturnsInt()
    {
        var node = new SchemaNode { Type = "integer", Rules = new ValidationRules { Format = "int32" } };
        _sut.Map(node).ShouldBe("int");
    }

    [Fact]
    public void Map_IntegerInt64Format_ReturnsLong()
    {
        var node = new SchemaNode { Type = "integer", Rules = new ValidationRules { Format = "int64" } };
        _sut.Map(node).ShouldBe("long");
    }

    [Fact]
    public void Map_NumberNoFormat_ReturnsDouble()
    {
        var node = new SchemaNode { Type = "number" };
        _sut.Map(node).ShouldBe("double");
    }

    [Fact]
    public void Map_NumberFloatFormat_ReturnsFloat()
    {
        var node = new SchemaNode { Type = "number", Rules = new ValidationRules { Format = "float" } };
        _sut.Map(node).ShouldBe("float");
    }

    [Fact]
    public void Map_NumberDoubleFormat_ReturnsDouble()
    {
        var node = new SchemaNode { Type = "number", Rules = new ValidationRules { Format = "double" } };
        _sut.Map(node).ShouldBe("double");
    }

    // ═══════════════════════════════════════════════════════════════
    // BOOLEAN
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_Boolean_ReturnsBool()
    {
        var node = new SchemaNode { Type = "boolean" };
        _sut.Map(node).ShouldBe("bool");
    }

    // ═══════════════════════════════════════════════════════════════
    // ARRAY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_ArrayOfStrings_ReturnsListOfString()
    {
        var node = new SchemaNode
        {
            Type = "array",
            Items = new SchemaNode { Type = "string" }
        };

        _sut.Map(node).ShouldBe("List<string>");
    }

    [Fact]
    public void Map_ArrayOfIntegers_ReturnsListOfInt()
    {
        var node = new SchemaNode
        {
            Type = "array",
            Items = new SchemaNode { Type = "integer" }
        };

        _sut.Map(node).ShouldBe("List<int>");
    }

    [Fact]
    public void Map_ArrayOfObjects_ReturnsListOfSchemaName()
    {
        var node = new SchemaNode
        {
            Type = "array",
            Items = new SchemaNode { Type = "object", Name = "PersonInfo" }
        };

        _sut.Map(node).ShouldBe("List<PersonInfo>");
    }

    [Fact]
    public void Map_ArrayWithoutItems_ReturnsListOfObject()
    {
        var node = new SchemaNode { Type = "array" };
        _sut.Map(node).ShouldBe("List<object>");
    }

    // ═══════════════════════════════════════════════════════════════
    // OBJECT / ENUM — named types
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_ObjectWithName_ReturnsClassName()
    {
        var node = new SchemaNode { Type = "object", Name = "PersonInfo" };
        _sut.Map(node).ShouldBe("PersonInfo");
    }

    [Fact]
    public void Map_EnumWithName_ReturnsEnumName()
    {
        var node = new SchemaNode
        {
            Type = "string",
            Name = "DocumentType",
            EnumValues = new[] { "PASSPORT", "FOREIGN_PASSPORT" }
        };

        _sut.Map(node).ShouldBe("DocumentType");
    }

    [Fact]
    public void Map_ObjectWithoutName_ReturnsObject()
    {
        var node = new SchemaNode { Type = "object" };
        _sut.Map(node).ShouldBe("object");
    }

    // ═══════════════════════════════════════════════════════════════
    // NULLABLE handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_NullableString_ReturnsNullableString()
    {
        var node = new SchemaNode { Type = "string", Rules = new ValidationRules { Nullable = true } };
        _sut.Map(node, applyNullable: true).ShouldBe("string?");
    }

    [Fact]
    public void Map_NullableInteger_ReturnsNullableInt()
    {
        var node = new SchemaNode { Type = "integer", Rules = new ValidationRules { Nullable = true } };
        _sut.Map(node, applyNullable: true).ShouldBe("int?");
    }

    [Fact]
    public void Map_NullableObject_ReturnsNullableClassName()
    {
        var node = new SchemaNode { Type = "object", Name = "PersonInfo", Rules = new ValidationRules { Nullable = true } };
        _sut.Map(node, applyNullable: true).ShouldBe("PersonInfo?");
    }

    [Fact]
    public void Map_NotNullable_DoesNotAddQuestionMark()
    {
        var node = new SchemaNode { Type = "string", Rules = new ValidationRules { Nullable = false } };
        _sut.Map(node, applyNullable: true).ShouldBe("string");
    }

    [Fact]
    public void Map_ApplyNullableFalse_IgnoresNullableFlag()
    {
        var node = new SchemaNode { Type = "string", Rules = new ValidationRules { Nullable = true } };
        _sut.Map(node, applyNullable: false).ShouldBe("string");
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARD
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Map_NullNode_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => _sut.Map(null!));
    }

    [Fact]
    public void Map_UnknownType_ReturnsObject()
    {
        var node = new SchemaNode { Type = "unknown_weird_type" };
        _sut.Map(node).ShouldBe("object");
    }
}
