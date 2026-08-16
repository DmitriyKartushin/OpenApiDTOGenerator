namespace OpenApiDtoGenerator.Core.Models;

/// <summary>
/// Тип источника $ref ссылки
/// </summary>
public enum RefKind
{
    /// <summary>#/components/schemas/Foo</summary>
    Local,

    /// <summary>./schemas/foo.yaml  или  ../shared/common.yaml#/Foo</summary>
    RelativeFile,

    /// <summary>/abs/path/to/spec.yaml  или  C:/specs/foo.yaml</summary>
    AbsoluteFile,

    /// <summary>https://example.com/specs/common.yaml#/Foo</summary>
    Remote
}
