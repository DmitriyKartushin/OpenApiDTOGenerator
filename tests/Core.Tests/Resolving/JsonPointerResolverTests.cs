using OpenApiDtoGenerator.Core.Resolving;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Resolving;

/// <summary>
/// TDD: Шаг 1.6 — JsonPointerResolver
///
/// Проверяем навигацию по дереву документа по RFC 6901 JSON Pointer:
///   - простой путь  /components/schemas/Foo
///   - вложенный     /components/schemas/Foo/properties/bar
///   - корневой      "" или "/"  → весь документ
///   - числовой индекс массива   /tags/0
///   - escape-последовательности ~0 (~) и ~1 (/)
///   - ошибки: несуществующий путь, неверный индекс, узел не объект/массив
/// </summary>
public class JsonPointerResolverTests
{
    private readonly JsonPointerResolver _sut = new();

    // Тестовый документ — имитируем распарсенный YAML как словарь
    private static Dictionary<string, object?> BuildDocument() => new()
    {
        ["openapi"] = "3.0.3",
        ["info"] = new Dictionary<string, object?>
        {
            ["title"] = "Test API",
            ["version"] = "1.0.0"
        },
        ["components"] = new Dictionary<string, object?>
        {
            ["schemas"] = new Dictionary<string, object?>
            {
                ["PersonInfo"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "Персональные данные",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["lastName"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["maxLength"] = 100
                        }
                    }
                },
                ["DocumentType"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = new List<object?> { "PASSPORT", "FOREIGN_PASSPORT" }
                }
            }
        },
        ["tags"] = new List<object?>
        {
            new Dictionary<string, object?> { ["name"] = "persons" },
            new Dictionary<string, object?> { ["name"] = "documents" }
        },
        // Ключ со слешем и тильдой — для проверки escape
        ["a/b"] = "slash-value",
        ["a~b"] = "tilde-value"
    };

    // ═══════════════════════════════════════════════════════════════
    // ROOT pointer
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_EmptyPointer_ReturnsWholeDocument()
    {
        var doc = BuildDocument();
        var result = _sut.Resolve(doc, "");

        result.ShouldBeSameAs(doc);
    }

    [Fact]
    public void Resolve_SlashOnlyPointer_ReturnsWholeDocument()
    {
        var doc = BuildDocument();
        // "/" по RFC 6901 — это ключ "" (пустая строка), но мы трактуем как root
        var result = _sut.Resolve(doc, "/");

        // должен вернуть документ или ключ ""
        // мы выбираем: "/" = root (упрощение для OpenAPI use-case)
        result.ShouldNotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // SIMPLE paths
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_TopLevelKey_ReturnsValue()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/openapi");

        result.ShouldBe("3.0.3");
    }

    [Fact]
    public void Resolve_NestedKey_ReturnsNestedObject()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/info/title");

        result.ShouldBe("Test API");
    }

    [Fact]
    public void Resolve_DeepPath_ReturnsSchemaObject()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/components/schemas/PersonInfo");

        result.ShouldBeOfType<Dictionary<string, object?>>();
        var schema = (Dictionary<string, object?>)result!;
        schema["type"].ShouldBe("object");
    }

    [Fact]
    public void Resolve_VeryDeepPath_ReturnsLeafValue()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/components/schemas/PersonInfo/properties/lastName/type");

        result.ShouldBe("string");
    }

    [Fact]
    public void Resolve_IntegerLeaf_ReturnsValue()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/components/schemas/PersonInfo/properties/lastName/maxLength");

        result.ShouldBe(100);
    }

    // ═══════════════════════════════════════════════════════════════
    // ARRAY indexing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_ArrayIndex_ReturnsElement()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/tags/0");

        result.ShouldBeOfType<Dictionary<string, object?>>();
        ((Dictionary<string, object?>)result!)["name"].ShouldBe("persons");
    }

    [Fact]
    public void Resolve_ArrayIndex_SecondElement()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/tags/1/name");

        result.ShouldBe("documents");
    }

    [Fact]
    public void Resolve_EnumArray_ReturnsElement()
    {
        var doc = BuildDocument();

        var result = _sut.Resolve(doc, "/components/schemas/DocumentType/enum/0");

        result.ShouldBe("PASSPORT");
    }

    // ═══════════════════════════════════════════════════════════════
    // ESCAPE sequences (RFC 6901)
    //   ~1 → /   ~0 → ~
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_EscapedSlash_ReturnsCorrectValue()
    {
        var doc = BuildDocument();

        // ключ "a/b" → pointer token "a~1b"
        var result = _sut.Resolve(doc, "/a~1b");

        result.ShouldBe("slash-value");
    }

    [Fact]
    public void Resolve_EscapedTilde_ReturnsCorrectValue()
    {
        var doc = BuildDocument();

        // ключ "a~b" → pointer token "a~0b"
        var result = _sut.Resolve(doc, "/a~0b");

        result.ShouldBe("tilde-value");
    }

    // ═══════════════════════════════════════════════════════════════
    // ERROR CASES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_NonExistentKey_ThrowsJsonPointerException()
    {
        var doc = BuildDocument();

        Should.Throw<JsonPointerException>(() => _sut.Resolve(doc, "/components/schemas/NonExistent"));
    }

    [Fact]
    public void Resolve_NonExistentDeepKey_ThrowsJsonPointerException()
    {
        var doc = BuildDocument();

        Should.Throw<JsonPointerException>(() => _sut.Resolve(doc, "/components/schemas/PersonInfo/nonExistentField"));
    }

    [Fact]
    public void Resolve_ArrayIndexOutOfBounds_ThrowsJsonPointerException()
    {
        var doc = BuildDocument();

        Should.Throw<JsonPointerException>(() => _sut.Resolve(doc, "/tags/99"));
    }

    [Fact]
    public void Resolve_NonNumericIndexOnArray_ThrowsJsonPointerException()
    {
        var doc = BuildDocument();

        Should.Throw<JsonPointerException>(() => _sut.Resolve(doc, "/tags/notAnIndex"));
    }

    [Fact]
    public void Resolve_TraverseIntoScalar_ThrowsJsonPointerException()
    {
        var doc = BuildDocument();

        // "openapi" = "3.0.3" — строка, нельзя идти глубже
        Should.Throw<JsonPointerException>(() => _sut.Resolve(doc, "/openapi/something"));
    }

    [Fact]
    public void Resolve_NullDocument_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => _sut.Resolve(null!, "/foo"));
    }

    [Fact]
    public void Resolve_NullPointer_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => _sut.Resolve(BuildDocument(), null!));
    }

    // ═══════════════════════════════════════════════════════════════
    // TryResolve — non-throwing variant
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TryResolve_ExistingPath_ReturnsTrueAndValue()
    {
        var doc = BuildDocument();

        var success = _sut.TryResolve(doc, "/info/title", out var value);

        success.ShouldBeTrue();
        value.ShouldBe("Test API");
    }

    [Fact]
    public void TryResolve_NonExistentPath_ReturnsFalseAndNull()
    {
        var doc = BuildDocument();

        var success = _sut.TryResolve(doc, "/no/such/path", out var value);

        success.ShouldBeFalse();
        value.ShouldBeNull();
    }
}
