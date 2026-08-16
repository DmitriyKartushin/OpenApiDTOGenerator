namespace OpenApiDtoGenerator.Core.Models;

/// <summary>
/// Результат разбора строки $ref на составные части.
/// </summary>
/// <param name="Kind">Тип источника ссылки.</param>
/// <param name="Source">
///   Путь к файлу или URI (null для локальных ссылок внутри текущего документа).
/// </param>
/// <param name="JsonPointer">
///   Путь внутри документа после символа '#' (например /components/schemas/Foo).
///   null — если якорь отсутствует (ссылка на весь документ).
/// </param>
public sealed record ParsedRef(
    RefKind Kind,
    string? Source,
    string? JsonPointer
)
{
    /// <summary>Является ли ссылка чисто локальной (без указания файла/URI).</summary>
    public bool IsLocal => Kind == RefKind.Local;

    /// <summary>Указывает ли ссылка на внешний документ (файл или HTTP).</summary>
    public bool IsExternal => !IsLocal;

    /// <summary>Есть ли JSON Pointer (часть после #).</summary>
    public bool HasPointer => JsonPointer is not null;
}
