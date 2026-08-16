namespace OpenApiDtoGenerator.Generators.Dto;

/// <summary>
/// Результат генерации одного файла — имя файла + текстовое содержимое.
/// </summary>
public sealed record GeneratedFile(string FileName, string Content);
