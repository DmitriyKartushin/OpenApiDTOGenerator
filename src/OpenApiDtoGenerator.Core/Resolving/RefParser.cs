using OpenApiDtoGenerator.Core.Models;

namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Разбирает строку $ref на составные части: Kind, Source, JsonPointer.
///
/// Поддерживаемые форматы:
///   Local        — "#/components/schemas/Foo"
///   RelativeFile — "./foo.yaml"  "../shared/foo.yaml"  "sub/foo.yaml"
///   AbsoluteFile — "/abs/path/foo.yaml"  "C:/path/foo.yaml"
///   Remote       — "https://..."  "http://..."
/// </summary>
public sealed class RefParser
{
    /// <summary>
    /// Разбирает строку $ref.
    /// </summary>
    /// <param name="refString">Значение поля $ref из YAML/JSON.</param>
    /// <returns><see cref="ParsedRef"/> с Kind, Source и JsonPointer.</returns>
    /// <exception cref="ArgumentException">Если refString пустой или null.</exception>
    public ParsedRef Parse(string refString)
    {
        if (string.IsNullOrWhiteSpace(refString))
            throw new ArgumentException("$ref value must not be null or whitespace.", nameof(refString));

        // ── 1. LOCAL: начинается с '#' ────────────────────────────────────────
        if (refString.StartsWith('#'))
        {
            // "#"              → pointer = ""
            // "#/a/b"          → pointer = "/a/b"
            var pointer = refString.Length > 1
                ? refString[1..]   // всё после '#'
                : string.Empty;

            return new ParsedRef(RefKind.Local, Source: null, JsonPointer: pointer);
        }

        // ── 2. REMOTE: http:// или https:// ──────────────────────────────────
        if (refString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            refString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return SplitFileAndPointer(refString, RefKind.Remote);
        }

        // ── 3. ABSOLUTE FILE ─────────────────────────────────────────────────
        //    Unix:    /abs/path/...
        //    Windows: C:/path/...  или  C:\path\...
        if (IsAbsolutePath(refString))
        {
            return SplitFileAndPointer(refString, RefKind.AbsoluteFile);
        }

        // ── 4. RELATIVE FILE — всё остальное ─────────────────────────────────
        //    ./foo.yaml  ../foo.yaml  foo.yaml  sub/dir/foo.yaml
        return SplitFileAndPointer(refString, RefKind.RelativeFile);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Разделяет строку вида "path/to/file.yaml#/pointer" на Source и JsonPointer.
    /// Символ '#' является разделителем.
    /// </summary>
    private static ParsedRef SplitFileAndPointer(string refString, RefKind kind)
    {
        var hashIndex = refString.IndexOf('#');

        if (hashIndex < 0)
        {
            // нет якоря — весь ref это путь/URI
            return new ParsedRef(kind, Source: refString, JsonPointer: null);
        }

        var source  = refString[..hashIndex];                      // до '#'
        var pointer = hashIndex < refString.Length - 1
            ? refString[(hashIndex + 1)..]                         // после '#'
            : string.Empty;

        return new ParsedRef(kind, Source: source, JsonPointer: pointer);
    }

    /// <summary>
    /// Проверяет, является ли путь абсолютным (Unix или Windows).
    /// </summary>
    private static bool IsAbsolutePath(string path)
    {
        // Unix-абсолютный: начинается с '/'
        if (path.StartsWith('/'))
            return true;

        // Windows-абсолютный: X:/ или X:\
        if (path.Length >= 3 &&
            char.IsLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '/' || path[2] == '\\'))
            return true;

        return false;
    }
}
