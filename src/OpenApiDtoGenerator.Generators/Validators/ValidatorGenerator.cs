using System.Text;
using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Generators.Dto;

namespace OpenApiDtoGenerator.Generators.Validators;

/// <summary>
/// Генерирует FluentValidation AbstractValidator&lt;T&gt; классы из SchemaNode (IR).
///
/// Возможности:
///   - Правила для каждого свойства (NotEmpty, MinimumLength, Matches, ...)
///   - Каскадная валидация вложенных объектов (SetValidator)
///   - Условная валидация через discriminator (.When(...))
///   - Поддержка prefix/suffix именования
/// </summary>
public sealed class ValidatorGenerator
{
    private readonly RuleMapper _ruleMapper = new();

    /// <summary>
    /// Генерирует .cs файл с валидатором для одной схемы.
    /// </summary>
    public Dto.GeneratedFile Generate(SchemaNode schema, DtoValidatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (string.IsNullOrEmpty(schema.Name))
            throw new InvalidOperationException("Schema must have a Name to generate a validator.");

        var className = $"{schema.Name}{options.ClassSuffix}Validator";
        var dtoName   = schema.Name;

        var sb = new StringBuilder();

        // ── Usings ───────────────────────────────────────────────────────────
        sb.AppendLine("using FluentValidation;");
        sb.AppendLine();
        sb.AppendLine($"namespace {options.Namespace};");
        sb.AppendLine();

        // ── Class declaration ─────────────────────────────────────────────────
        sb.AppendLine($"public class {className} : AbstractValidator<{dtoName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public {className}()");
        sb.AppendLine("    {");

        // ── Property rules ────────────────────────────────────────────────────
        foreach (var prop in schema.Properties)
        {
            AppendPropertyRules(sb, prop, schema);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new Dto.GeneratedFile($"{className}.cs", sb.ToString());
    }

    /// <summary>
    /// Генерирует валидаторы для всех object-схем (enum'ы пропускаются).
    /// </summary>
    public List<Dto.GeneratedFile> GenerateAll(
        IReadOnlyDictionary<string, SchemaNode> schemas,
        DtoValidatorOptions options)
    {
        var results = new List<Dto.GeneratedFile>();

        foreach (var schema in schemas.Values)
        {
            // Enum-схемы не нуждаются в отдельном валидаторе
            if (schema.IsEnum)
                continue;

            // Схемы без имени пропускаем
            if (string.IsNullOrEmpty(schema.Name))
                continue;

            results.Add(Generate(schema, options));
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core rule generation
    // ─────────────────────────────────────────────────────────────────────────

    private void AppendPropertyRules(StringBuilder sb, PropertyNode prop, SchemaNode parentSchema)
    {
        var propName = NameHelper.ToPascalCase(prop.Name);
        var schema   = prop.Schema;

        // ── Собираем примитивные правила через RuleMapper ─────────────────────
        var primitiveRules = _ruleMapper.Map(prop);

        // ── Определяем нужен ли SetValidator (вложенный объект) ──────────────
        var needsSetValidator = schema.IsObject
            && !string.IsNullOrEmpty(schema.Name)
            && !schema.IsEnum;

        // ── Определяем нужен ли When (discriminator) ─────────────────────────
        var discriminatorProp = parentSchema.DiscriminatorPropertyName;
        var discriminatorValue = GetDiscriminatorValue(prop.Name, parentSchema);
        var needsWhen = discriminatorProp != null && discriminatorValue != null;

        // Если нет никаких правил — пропускаем свойство
        if (!primitiveRules.Any() && !needsSetValidator)
            return;

        sb.AppendLine();

        // ── Начало цепочки: RuleFor ───────────────────────────────────────────
        sb.Append($"        RuleFor(x => x.{propName})");

        // ── Примитивные правила (каждое на новой строке для читаемости) ───────
        foreach (var rule in primitiveRules)
        {
            sb.AppendLine();
            sb.Append($"            .{rule}");
        }

        // ── SetValidator для вложенных объектов ───────────────────────────────
        if (needsSetValidator)
        {
            sb.AppendLine();
            sb.Append($"            .SetValidator(new {schema.Name}Validator())");
        }

        // ── When для discriminator-зависимых полей ────────────────────────────
        if (needsWhen)
        {
            var discriminatorPropName = NameHelper.ToPascalCase(discriminatorProp!);
            sb.AppendLine();
            sb.Append($"            .When(x => x.{discriminatorPropName} == \"{discriminatorValue}\")");
        }

        sb.AppendLine(";");
    }

    /// <summary>
    /// Ищет значение дискриминатора для данного свойства.
    /// Например: passport → "PASSPORT" (если в DiscriminatorMapping есть "PASSPORT" → "PassportInfo").
    /// </summary>
    private static string? GetDiscriminatorValue(string propName, SchemaNode parentSchema)
    {
        if (parentSchema.DiscriminatorMapping.Count == 0)
            return null;

        // Ищем значение дискриминатора, чья схема соответствует имени свойства
        // Эвристика: "passport" → ищем схему, содержащую "Passport" в имени
        foreach (var (discriminatorValue, schemaName) in parentSchema.DiscriminatorMapping)
        {
            if (schemaName.Contains(NameHelper.ToPascalCase(propName), StringComparison.OrdinalIgnoreCase)
                || propName.Contains(discriminatorValue.ToLowerInvariant().Replace("_", ""), StringComparison.OrdinalIgnoreCase))
            {
                return discriminatorValue;
            }
        }

        return null;
    }
}
