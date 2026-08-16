using OpenApiDtoGenerator.Core.Parsing;
using Shouldly;
using Xunit;
namespace OpenApiDtoGenerator.Core.Tests.Parsing;

public class JsonDocumentParserTests
{
    private readonly JsonDocumentParser _sut = new();

    [Fact]
    public void Parse_ValidJsonObject_ReturnsDict()
    {
        const string json = """{ "key": "value", "number": 42 }""";

        var result = _sut.Parse(json);

        result.ShouldBeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)result!;
        dict["key"].ShouldBe("value");
        dict["number"].ShouldBe(42);
    }

    [Fact]
    public void Parse_ValidJsonArray_ReturnsList()
    {
        const string json = """[ "a", "b", 1, true, null ]""";

        var result = _sut.Parse(json);

        result.ShouldBeOfType<List<object?>>();
        var list = (List<object?>)result!;
        list.Count.ShouldBe(5);
        list[0].ShouldBe("a");
        list[2].ShouldBe(1);
        list[3].ShouldBe(true);
        list[4].ShouldBeNull();
    }

    [Fact]
    public void Parse_NestedStructure_PreservesNesting()
    {
        const string json = """
        {
            "outer": {
                "inner": [ 1, 2, 3 ]
            }
        }
        """;

        var result = _sut.Parse(json);

        var dict = (Dictionary<string, object?>)result!;
        var outer = (Dictionary<string, object?>)dict["outer"]!;
        var inner = (List<object?>)outer["inner"]!;
        inner.Count.ShouldBe(3);
    }

    [Fact]
    public void Parse_EmptyJson_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => _sut.Parse(""));
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => _sut.Parse("{ invalid json }"));
    }
}
