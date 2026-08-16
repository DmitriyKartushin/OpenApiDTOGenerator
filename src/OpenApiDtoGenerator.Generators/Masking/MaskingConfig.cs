namespace OpenApiDtoGenerator.Generators.Masking;

/// <summary>
/// Правило маскировки: список полей + стратегия + параметры.
/// </summary>
/// <param name="Fields">Список имён полей (case-insensitive).</param>
/// <param name="Strategy">Стратегия маскировки.</param>
/// <param name="VisibleStart">Для Partial: сколько символов слева видно.</param>
/// <param name="VisibleEnd">Для Partial: сколько символов справа видно.</param>
public sealed record MaskingRule(
    IReadOnlyList<string> Fields,
    MaskStrategy Strategy,
    int VisibleStart = 0,
    int VisibleEnd = 0);

/// <summary>
/// Конфигурация маскировки — набор правил + метод поиска правила по имени поля.
/// </summary>
public sealed class MaskingConfig
{
    private readonly IReadOnlyList<MaskingRule> _rules;

    // fieldName (lowercase) → rule для быстрого lookup
    private readonly Dictionary<string, MaskingRule> _lookup;

    public MaskingConfig(IReadOnlyList<MaskingRule> rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));

        _lookup = new Dictionary<string, MaskingRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            foreach (var field in rule.Fields)
            {
                // Если поле упомянуто в нескольких правилах — берём первое
                _lookup.TryAdd(field, rule);
            }
        }
    }

    /// <summary>Все правила конфига.</summary>
    public IReadOnlyList<MaskingRule> Rules => _rules;

    /// <summary>
    /// Ищет правило маскировки для указанного поля (case-insensitive).
    /// Возвращает null если поле не подпадает под маскировку.
    /// </summary>
    public MaskingRule? FindRuleForField(string fieldName)
        => _lookup.TryGetValue(fieldName, out var rule) ? rule : null;

    /// <summary>Пустая конфигурация — без маскировки.</summary>
    public static readonly MaskingConfig Empty = new(Array.Empty<MaskingRule>());
}
