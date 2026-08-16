using System.Text;

namespace OpenApiDtoGenerator.Generators.Dto;

/// <summary>
/// Утилиты для преобразования имён (camelCase YAML → PascalCase C#).
/// </summary>
public static class NameHelper
{
    /// <summary>
    /// Преобразует camelCase/snake_case в PascalCase.
    /// "firstName" → "FirstName", "document_type" → "DocumentType".
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Разбиваем по подчёркиваниям/дефисам, каждую часть капитализируем
        var parts = name.Split(['_', '-']);
        var sb = new StringBuilder();

        foreach (var part in parts)
        {
            if (part.Length == 0)
                continue;

            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                sb.Append(part[1..]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Строит итоговое имя класса с учётом префикса/суффикса.
    /// </summary>
    public static string BuildClassName(string schemaName, string prefix, string suffix)
        => $"{prefix}{schemaName}{suffix}";
}
