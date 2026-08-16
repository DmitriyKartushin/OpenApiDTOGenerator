using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Core.Resolving;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Resolving;

/// <summary>
/// TDD: Шаг 1.2 — RefParser
///
/// Цикл: запускаем тесты → все RED → пишем реализацию → все GREEN
/// </summary>
public class RefParserTests
{
    private readonly RefParser _sut = new();

    // ═══════════════════════════════════════════════════════════════
    // LOCAL refs   (#/...)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_LocalRef_ReturnsLocalKind()
    {
        var result = _sut.Parse("#/components/schemas/PersonInfo");

        result.Kind.ShouldBe(RefKind.Local);
    }

    [Fact]
    public void Parse_LocalRef_SourceIsNull()
    {
        var result = _sut.Parse("#/components/schemas/PersonInfo");

        result.Source.ShouldBeNull();
    }

    [Fact]
    public void Parse_LocalRef_CorrectPointer()
    {
        var result = _sut.Parse("#/components/schemas/PersonInfo");

        result.JsonPointer.ShouldBe("/components/schemas/PersonInfo");
    }

    [Fact]
    public void Parse_LocalRef_RequestBody_CorrectPointer()
    {
        var result = _sut.Parse("#/components/requestBodies/CreatePersonRequest");

        result.Kind.ShouldBe(RefKind.Local);
        result.JsonPointer.ShouldBe("/components/requestBodies/CreatePersonRequest");
    }

    [Fact]
    public void Parse_LocalRef_RootOnly_PointerIsEmpty()
    {
        // edge case: просто "#"
        var result = _sut.Parse("#");

        result.Kind.ShouldBe(RefKind.Local);
        result.JsonPointer.ShouldBe(string.Empty);
    }

    // ═══════════════════════════════════════════════════════════════
    // RELATIVE FILE refs   (./... или ../...)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_RelativeFileRef_NoPointer_ReturnsRelativeFileKind()
    {
        var result = _sut.Parse("./schemas/person.yaml");

        result.Kind.ShouldBe(RefKind.RelativeFile);
    }

    [Fact]
    public void Parse_RelativeFileRef_NoPointer_SourceIsPath()
    {
        var result = _sut.Parse("./schemas/person.yaml");

        result.Source.ShouldBe("./schemas/person.yaml");
    }

    [Fact]
    public void Parse_RelativeFileRef_NoPointer_JsonPointerIsNull()
    {
        var result = _sut.Parse("./schemas/person.yaml");

        result.JsonPointer.ShouldBeNull();
    }

    [Fact]
    public void Parse_RelativeFileRef_WithPointer_SplitsCorrectly()
    {
        var result = _sut.Parse("./schemas/person.yaml#/components/schemas/PersonInfo");

        result.Kind.ShouldBe(RefKind.RelativeFile);
        result.Source.ShouldBe("./schemas/person.yaml");
        result.JsonPointer.ShouldBe("/components/schemas/PersonInfo");
    }

    [Fact]
    public void Parse_RelativeFileRef_ParentDir_ReturnsRelativeFileKind()
    {
        var result = _sut.Parse("../shared/common.yaml#/schemas/Foo");

        result.Kind.ShouldBe(RefKind.RelativeFile);
        result.Source.ShouldBe("../shared/common.yaml");
        result.JsonPointer.ShouldBe("/schemas/Foo");
    }

    [Fact]
    public void Parse_RelativeFileRef_DeepPath_CorrectSource()
    {
        var result = _sut.Parse("./schemas/documents/passport.yaml");

        result.Kind.ShouldBe(RefKind.RelativeFile);
        result.Source.ShouldBe("./schemas/documents/passport.yaml");
    }

    [Theory]
    [InlineData("./person.yaml")]
    [InlineData("../shared.yaml")]
    [InlineData("schemas/person.yaml")]          // без ./ тоже относительный
    [InlineData("sub/dir/file.yaml")]
    public void Parse_RelativeFileRef_VariousFormats_AllRelative(string refString)
    {
        var result = _sut.Parse(refString);

        result.Kind.ShouldBe(RefKind.RelativeFile);
    }

    // ═══════════════════════════════════════════════════════════════
    // ABSOLUTE FILE refs   (/abs/path или C:/path)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_AbsoluteFileRef_Unix_ReturnsAbsoluteFileKind()
    {
        var result = _sut.Parse("/home/user/specs/common.yaml");

        result.Kind.ShouldBe(RefKind.AbsoluteFile);
    }

    [Fact]
    public void Parse_AbsoluteFileRef_Windows_ReturnsAbsoluteFileKind()
    {
        var result = _sut.Parse("C:/specs/common.yaml");

        result.Kind.ShouldBe(RefKind.AbsoluteFile);
    }

    [Fact]
    public void Parse_AbsoluteFileRef_WithPointer_SplitsCorrectly()
    {
        var result = _sut.Parse("/home/user/specs/common.yaml#/schemas/Foo");

        result.Kind.ShouldBe(RefKind.AbsoluteFile);
        result.Source.ShouldBe("/home/user/specs/common.yaml");
        result.JsonPointer.ShouldBe("/schemas/Foo");
    }

    // ═══════════════════════════════════════════════════════════════
    // REMOTE refs   (http:// или https://)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_RemoteRef_Https_ReturnsRemoteKind()
    {
        var result = _sut.Parse("https://api.example.com/specs/common.yaml");

        result.Kind.ShouldBe(RefKind.Remote);
    }

    [Fact]
    public void Parse_RemoteRef_Http_ReturnsRemoteKind()
    {
        var result = _sut.Parse("http://api.example.com/specs/common.yaml");

        result.Kind.ShouldBe(RefKind.Remote);
    }

    [Fact]
    public void Parse_RemoteRef_NoPointer_SourceIsFullUri()
    {
        var result = _sut.Parse("https://api.example.com/specs/common.yaml");

        result.Source.ShouldBe("https://api.example.com/specs/common.yaml");
        result.JsonPointer.ShouldBeNull();
    }

    [Fact]
    public void Parse_RemoteRef_WithPointer_SplitsCorrectly()
    {
        var result = _sut.Parse("https://api.example.com/specs/common.yaml#/schemas/Foo");

        result.Kind.ShouldBe(RefKind.Remote);
        result.Source.ShouldBe("https://api.example.com/specs/common.yaml");
        result.JsonPointer.ShouldBe("/schemas/Foo");
    }

    [Fact]
    public void Parse_RemoteRef_WithDeepPointer_CorrectPointer()
    {
        var result = _sut.Parse("https://example.com/specs/v2/api.yaml#/components/schemas/PersonInfo");

        result.JsonPointer.ShouldBe("/components/schemas/PersonInfo");
        result.Source.ShouldBe("https://example.com/specs/v2/api.yaml");
    }

    // ═══════════════════════════════════════════════════════════════
    // IsLocal / IsExternal / HasPointer — computed properties
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ParsedRef_Local_IsLocal_True()
    {
        var result = _sut.Parse("#/components/schemas/Foo");
        result.IsLocal.ShouldBeTrue();
        result.IsExternal.ShouldBeFalse();
    }

    [Fact]
    public void ParsedRef_Remote_IsExternal_True()
    {
        var result = _sut.Parse("https://example.com/spec.yaml");
        result.IsLocal.ShouldBeFalse();
        result.IsExternal.ShouldBeTrue();
    }

    [Fact]
    public void ParsedRef_WithPointer_HasPointer_True()
    {
        var result = _sut.Parse("#/components/schemas/Foo");
        result.HasPointer.ShouldBeTrue();
    }

    [Fact]
    public void ParsedRef_WithoutPointer_HasPointer_False()
    {
        var result = _sut.Parse("./person.yaml");
        result.HasPointer.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARD — невалидный вход
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_NullInput_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => _sut.Parse(null!));
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => _sut.Parse(string.Empty));
    }

    [Fact]
    public void Parse_WhitespaceString_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => _sut.Parse("   "));
    }
}
