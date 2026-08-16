using OpenApiDtoGenerator.Core.Parsing;
using OpenApiDtoGenerator.Generators.Masking;

namespace OpenApiDtoGenerator.Cli.Config;

/// <summary>
/// Загружает MaskingConfig из YAML-файла.
///
/// Формат:
/// <code>
/// masking:
///   rules:
///     - fields: [series, number]
///       strategy: partial
///       visibleStart: 2
///       visibleEnd: 2
///     - fields: [lastName, firstName]
///       strategy: firstChar
///     - fields: [issuedBy]
///       strategy: full
/// </code>
/// </summary>
public sealed class MaskingConfigLoader
{
    private readonly YamlDocumentParser _parser = new();

    /// <summary>
    /// Загружает и парсит конфиг маскировки из YAML-файла.
    /// </summary>
    /// <exception cref="FileNotFoundException">Файл не найден.</exception>
    /// <exception cref="FormatException">Некорректная YAML-структура.</exception>
    public MaskingConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Masking config file not found: '{path}'", path);

        var content = File.ReadAllText(path);
        return LoadFromString(content);
    }

    /// <summary>Парсит YAML-строку в MaskingConfig.</summary>
    public MaskingConfig LoadFromString(string yamlContent)
    {
        var root = _parser.Parse(yamlContent) as Dictionary<string, object?>
            ?? throw new FormatException("Masking config root must be a mapping.");

        // Поддерживаем оба формата:
        //   1) корневой rules:  ...
        //   2) masking:\n  rules: ...
        var rulesNode = root.TryGetValue("masking", out var maskingObj) && maskingObj is Dictionary<string, object?> maskingDict
            ? maskingDict.GetValueOrDefault("rules")
            : root.GetValueOrDefault("rules");

        if (rulesNode is not List<object?> rulesList)
            throw new FormatException("Masking config must contain a 'rules' list.");

        var rules = new List<MaskingRule>();

        foreach (var ruleObj in rulesList)
        {
            if (ruleObj is not Dictionary<string, object?> ruleDict)
                continue;

            var fields = ExtractFields(ruleDict);
            var strategy = ExtractStrategy(ruleDict);
            var visibleStart = ExtractInt(ruleDict, "visibleStart") ?? 0;
            var visibleEnd = ExtractInt(ruleDict, "visibleEnd") ?? 0;

            rules.Add(new MaskingRule(fields, strategy, visibleStart, visibleEnd));
        }

        return new MaskingConfig(rules);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ExtractFields(Dictionary<string, object?> rule)
    {
        if (!rule.TryGetValue("fields", out var fieldsObj) || fieldsObj is not List<object?> fieldsList)
            throw new FormatException("Each masking rule must have a 'fields' list.");

        return fieldsList.OfType<string>().ToList();
    }

    private static MaskStrategy ExtractStrategy(Dictionary<string, object?> rule)
    {
        if (!rule.TryGetValue("strategy", out var strategyObj) || strategyObj is not string strategyStr)
            throw new FormatException("Each masking rule must have a 'strategy' field.");

        // case-insensitive парсинг
        if (!Enum.TryParse<MaskStrategy>(strategyStr, ignoreCase: true, out var strategy))
            throw new FormatException(
                $"Unknown masking strategy: '{strategyStr}'. " +
                $"Valid: {string.Join(", ", Enum.GetNames<MaskStrategy>())}");

        return strategy;
    }

    private static int? ExtractInt(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value)) return null;
        return value switch
        {
            int i => i,
            long l => (int)l,
            _ => null
        };
    }
}
