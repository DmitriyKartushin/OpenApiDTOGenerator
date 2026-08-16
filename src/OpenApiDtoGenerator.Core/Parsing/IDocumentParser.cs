using OpenApiDtoGenerator.Core.Models;

namespace OpenApiDtoGenerator.Core.Parsing;

/// <summary>
/// Контракт для парсинга raw YAML/JSON строки в типизированный документ.
/// Скрывает детали YamlDotNet/System.Text.Json.
/// </summary>
public interface IDocumentParser
{
    /// <summary>
    /// Парсит raw содержимое в дерево (Dictionary/List/скаляр).
    /// </summary>
    /// <param name="rawContent">YAML или JSON строка.</param>
    /// <returns>Распарсенное дерево — Dictionary, List или скаляр.</returns>
    /// <exception cref="FormatException">Если парсинг не удался.</exception>
    object? Parse(string rawContent);
}
