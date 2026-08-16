using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Generators.Dto;

namespace OpenApiDtoGenerator.Generators.Validators;

/// <summary>
/// Маппит ValidationRules + PropertyNode → список строк FluentValidation вызовов.
///
/// Каждая строка — это цепочка методов без ведущей точки, например:
///   "NotEmpty()"
///   "MinimumLength(1)"
///   "MaximumLength(100).WithMessage(\"Фамилия\")"
///   "Matches(@\"^\\d{4}$\")"
/// </summary>
public sealed class RuleMapper
{
    public IReadOnlyList<string> Map(PropertyNode prop)
    {
        ArgumentNullException.ThrowIfNull(prop);

        var rules = new List<string>();
        var schema = prop.Schema;

        // ── 1. Required → NotEmpty / NotNull ────────────────────────────────
        if (prop.IsRequired)
        {
            var notRule = schema.Type is "object" or "array" || !string.IsNullOrEmpty(schema.Name)
                ? "NotNull()"
                : "NotEmpty()";
            rules.Add(notRule);
        }

        // ── 2. Enum → IsInEnum ───────────────────────────────────────────────
        if (schema.IsEnum)
        {
            rules.Add("IsInEnum()");
            return rules; // для enum других правил нет
        }

        // ── 3. String rules ──────────────────────────────────────────────────
        if (schema.Type == "string")
        {
            AppendStringRules(rules, schema.Rules, prop.Description);
        }

        // ── 4. Numeric rules ─────────────────────────────────────────────────
        if (schema.Type is "integer" or "number")
        {
            AppendNumericRules(rules, schema.Rules);
        }

        // ── 5. Array rules ───────────────────────────────────────────────────
        if (schema.Type == "array")
        {
            AppendArrayRules(rules, schema.Rules);
        }

        return rules;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rule builders
    // ─────────────────────────────────────────────────────────────────────────

    private static void AppendStringRules(List<string> rules, ValidationRules r, string? description)
    {
        // MinLength / MaxLength
        if (r.MinLength.HasValue)
            rules.Add(WithMsg($"MinimumLength({r.MinLength.Value})", description));

        if (r.MaxLength.HasValue)
            rules.Add(WithMsg($"MaximumLength({r.MaxLength.Value})", description));

        // Pattern → Matches
        if (!string.IsNullOrEmpty(r.Pattern))
            rules.Add(WithMsg($@"Matches(@""{EscapePattern(r.Pattern)}"")", description));

        // Format
        switch (r.Format)
        {
            case "email":
                rules.Add("EmailAddress()");
                break;
            case "uuid":
                rules.Add(@"Must(v => v == null || Guid.TryParse(v, out _)).WithMessage(""Invalid UUID format"")");
                break;
            case "date":
                rules.Add(@"Must(v => v == null || DateOnly.TryParse(v, out _)).WithMessage(""Invalid date format (expected YYYY-MM-DD)"")");
                break;
            case "date-time":
                rules.Add(@"Must(v => v == null || DateTime.TryParse(v, out _)).WithMessage(""Invalid date-time format"")");
                break;
        }
    }

    private static void AppendNumericRules(List<string> rules, ValidationRules r)
    {
        if (r.Minimum.HasValue)
        {
            var method = r.ExclusiveMinimum
                ? $"GreaterThan({FormatNumber(r.Minimum.Value)})"
                : $"GreaterThanOrEqualTo({FormatNumber(r.Minimum.Value)})";
            rules.Add(method);
        }

        if (r.Maximum.HasValue)
        {
            var method = r.ExclusiveMaximum
                ? $"LessThan({FormatNumber(r.Maximum.Value)})"
                : $"LessThanOrEqualTo({FormatNumber(r.Maximum.Value)})";
            rules.Add(method);
        }
    }

    private static void AppendArrayRules(List<string> rules, ValidationRules r)
    {
        if (r.MinItems.HasValue)
            rules.Add($"Must(list => list == null || list.Count >= {r.MinItems.Value})" +
                      $@".WithMessage(""Must contain at least {r.MinItems.Value} item(s)"")");

        if (r.MaxItems.HasValue)
            rules.Add($"Must(list => list == null || list.Count <= {r.MaxItems.Value})" +
                      $@".WithMessage(""Must contain at most {r.MaxItems.Value} item(s)"")");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Добавляет .WithMessage("description") если описание задано.
    /// </summary>
    private static string WithMsg(string rule, string? description)
        => string.IsNullOrWhiteSpace(description)
            ? rule
            : $@"{rule}.WithMessage(""{description.Replace("\"", "\\\"")}"")";

    /// <summary>
    /// Форматирует число: целое без .0, дробное с точкой.
    /// </summary>
    private static string FormatNumber(double value)
        => value == Math.Floor(value) ? ((long)value).ToString() : value.ToString("G");

    /// <summary>
    /// Экранирует кавычки внутри паттерна для вставки в verbatim-строку.
    /// В verbatim-строках @"..." двойная кавычка экранируется как "".
    /// </summary>
    private static string EscapePattern(string pattern)
        => pattern.Replace("\"", "\"\"");
}
