using OpenApiDtoGenerator.Core.Parsing;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Parsing;

/// <summary>
/// TDD: YamlDocumentParser — парсинг реального YAML (не JSON) через YamlDotNet.
/// Результат должен быть в том же формате, что и JsonDocumentParser:
///   Dictionary&lt;string, object?&gt; / List&lt;object?&gt; / скаляры.
/// </summary>
public class YamlDocumentParserTests
{
    private readonly YamlDocumentParser _sut = new();

    [Fact]
    public void Parse_SimpleYamlObject_ReturnsDict()
    {
        const string yaml = "key: value\nnumber: 42";

        var result = _sut.Parse(yaml);

        result.ShouldBeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)result!;
        dict["key"].ShouldBe("value");
        dict["number"].ShouldBe(42);
    }

    [Fact]
    public void Parse_YamlList_ReturnsList()
    {
        const string yaml = "- a\n- b\n- c";

        var result = _sut.Parse(yaml);

        result.ShouldBeOfType<List<object?>>();
        var list = (List<object?>)result!;
        list.Count.ShouldBe(3);
        list[0].ShouldBe("a");
    }

    [Fact]
    public void Parse_NestedYaml_PreservesStructure()
    {
        const string yaml = """
        components:
          schemas:
            Foo:
              type: object
              properties:
                bar:
                  type: string
        """;

        var result = _sut.Parse(yaml);

        var dict = (Dictionary<string, object?>)result!;
        var components = (Dictionary<string, object?>)dict["components"]!;
        var schemas = (Dictionary<string, object?>)components["schemas"]!;
        var foo = (Dictionary<string, object?>)schemas["Foo"]!;
        foo["type"].ShouldBe("object");
    }

    [Fact]
    public void Parse_YamlWithRef_PreservesRefKey()
    {
        const string yaml = """
        properties:
          personInfo:
            $ref: '#/components/schemas/PersonInfo'
        """;

        var result = _sut.Parse(yaml);

        var dict = (Dictionary<string, object?>)result!;
        var props = (Dictionary<string, object?>)dict["properties"]!;
        var personInfo = (Dictionary<string, object?>)props["personInfo"]!;
        personInfo["$ref"].ShouldBe("#/components/schemas/PersonInfo");
    }

    [Fact]
    public void Parse_YamlWithCyrillicText_PreservesUnicode()
    {
        const string yaml = "description: Персональные данные физического лица";

        var result = _sut.Parse(yaml);

        var dict = (Dictionary<string, object?>)result!;
        dict["description"].ShouldBe("Персональные данные физического лица");
    }

    [Fact]
    public void Parse_YamlBoolean_ReturnsBool()
    {
        const string yaml = "required: true\noptional: false";

        var result = _sut.Parse(yaml);

        var dict = (Dictionary<string, object?>)result!;
        dict["required"].ShouldBe(true);
        dict["optional"].ShouldBe(false);
    }

    [Fact]
    public void Parse_YamlNull_ReturnsNull()
    {
        const string yaml = "value: null";

        var result = _sut.Parse(yaml);

        var dict = (Dictionary<string, object?>)result!;
        dict["value"].ShouldBeNull();
    }

    [Fact]
    public void Parse_QuotedNumericString_StaysString()
    {
        // Важно для паттернов типа '4510' — не должны превращаться в int
        const string yaml = "series: '4510'";

        var result = _sut.Parse(yaml);

        var dict = (Dictionary<string, object?>)result!;
        dict["series"].ShouldBe("4510");
    }

    [Fact]
    public void Parse_EmptyContent_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => _sut.Parse(""));
    }

    [Fact]
    public void Parse_InvalidYaml_ThrowsFormatException()
    {
        const string invalidYaml = "key: [unclosed";

        Should.Throw<FormatException>(() => _sut.Parse(invalidYaml));
    }
}
