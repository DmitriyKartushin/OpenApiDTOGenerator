using System.Text;
using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Generators.Masking;

namespace OpenApiDtoGenerator.Generators.Dto;

/// <summary>
/// Генерирует C# классы и enum'ы из SchemaNode (IR).
///
/// Возможности:
///   - object → class с [JsonPropertyName] атрибутами
///   - enum → C# enum с [EnumMember] атрибутами
///   - allOf с именованной базовой схемой → наследование (: BaseClass)
///   - required/optional → nullable annotations
///   - XML doc комментарии из Description
///   - Опционально: partial class (для расширения через masking/validation)
///   - Опционально: [Mask(...)] атрибуты на чувствительных полях
/// </summary>
public sealed class DtoGenerator
{
    private readonly TypeMapper _typeMapper = new();
    private readonly MaskingGenerator _maskingGenerator = new();

    /// <summary>
    /// Генерирует .cs файл для object-схемы (класс).
    /// </summary>
    public GeneratedFile GenerateClass(SchemaNode schema, DtoGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (string.IsNullOrEmpty(schema.Name))
            throw new InvalidOperationException("Schema must have a Name to generate a class.");

        var className = NameHelper.BuildClassName(schema.Name, options.ClassPrefix, options.ClassSuffix);

        var sb = new StringBuilder();

        // ── using'и ──────────────────────────────────────────────────────────
        sb.AppendLine("using System.Text.Json.Serialization;");
        if (options.MaskingConfig is not null && !string.IsNullOrEmpty(options.MaskingNamespace))
            sb.AppendLine($"using {options.MaskingNamespace};");
        sb.AppendLine();
        sb.AppendLine($"namespace {options.Namespace};");
        sb.AppendLine();

        // ── XML doc ──────────────────────────────────────────────────────────
        AppendXmlDoc(sb, schema.Description);

        // ── Class declaration (с учётом allOf → наследование) ─────────────
        var (baseClassName, extraProperties, _) = ResolveAllOf(schema);

        var partialKeyword = options.GeneratePartial ? "partial " : string.Empty;
        sb.Append($"public {partialKeyword}class {className}");
        if (baseClassName is not null)
            sb.Append($" : {baseClassName}");
        sb.AppendLine();
        sb.AppendLine("{");

        // ── Properties (собственные + из allOf) ─────────────────────────────
        var allProperties = schema.Properties.Concat(extraProperties).ToList();

        for (var i = 0; i < allProperties.Count; i++)
        {
            AppendProperty(sb, allProperties[i], options);
            if (i < allProperties.Count - 1)
                sb.AppendLine();
        }

        sb.AppendLine("}");

        return new GeneratedFile($"{className}.cs", sb.ToString());
    }

    /// <summary>
    /// Генерирует .cs файл для enum-схемы.
    /// </summary>
    public GeneratedFile GenerateEnum(SchemaNode schema, DtoGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (string.IsNullOrEmpty(schema.Name))
            throw new InvalidOperationException("Schema must have a Name to generate an enum.");

        var enumName = NameHelper.BuildClassName(schema.Name, options.ClassPrefix, options.ClassSuffix);

        var sb = new StringBuilder();

        sb.AppendLine("using System.Runtime.Serialization;");
        sb.AppendLine();
        sb.AppendLine($"namespace {options.Namespace};");
        sb.AppendLine();

        AppendXmlDoc(sb, schema.Description);

        sb.AppendLine($"public enum {enumName}");
        sb.AppendLine("{");

        for (var i = 0; i < schema.EnumValues.Count; i++)
        {
            var value = schema.EnumValues[i];
            var memberName = NameHelper.ToPascalCase(value.ToLowerInvariant());

            sb.AppendLine($"    [EnumMember(Value = \"{value}\")]");
            sb.Append($"    {memberName}");
            sb.AppendLine(i < schema.EnumValues.Count - 1 ? "," : "");
        }

        sb.AppendLine("}");

        return new GeneratedFile($"{enumName}.cs", sb.ToString());
    }

    /// <summary>
    /// Генерирует файлы для всех схем — автоматически определяет class vs enum.
    /// </summary>
    public List<GeneratedFile> GenerateAll(
        IReadOnlyDictionary<string, SchemaNode> schemas,
        DtoGeneratorOptions options)
    {
        var results = new List<GeneratedFile>();

        foreach (var schema in schemas.Values)
        {
            if (schema.IsEnum)
                results.Add(GenerateEnum(schema, options));
            else if (schema.IsObject)
                results.Add(GenerateClass(schema, options));
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void AppendProperty(StringBuilder sb, PropertyNode prop, DtoGeneratorOptions options)
    {
        var csType = _typeMapper.Map(prop.Schema, applyNullable: true);
        var propName = NameHelper.ToPascalCase(prop.Name);

        // Если свойство опциональное и тип — не nullable примитив/объект, добавляем "?"
        if (!prop.IsRequired && !csType.EndsWith('?') && !csType.StartsWith("List<"))
            csType += "?";

        if (!string.IsNullOrEmpty(prop.Description))
        {
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {prop.Description}");
            sb.AppendLine("    /// </summary>");
        }

        // [Mask(...)] атрибут — если masking-конфиг задан и поле в нём есть
        if (options.MaskingConfig is not null)
        {
            var maskAttr = _maskingGenerator.GetMaskAttributeForProperty(prop.Name, options.MaskingConfig);
            if (maskAttr is not null)
                sb.AppendLine($"    {maskAttr}");
        }

        sb.AppendLine($"    [JsonPropertyName(\"{prop.Name}\")]");
        sb.AppendLine($"    public {csType} {propName} {{ get; set; }}");
    }

    private static void AppendXmlDoc(StringBuilder sb, string? description)
    {
        if (string.IsNullOrEmpty(description))
            return;

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// {description}");
        sb.AppendLine("/// </summary>");
    }

    /// <summary>
    /// Разбирает allOf: первая именованная схема без properties → базовый класс (наследование).
    /// Остальные схемы (обычно inline с properties) → добавляются как собственные свойства.
    /// </summary>
    private static (string? baseClassName, List<PropertyNode> extraProperties, List<string> descriptions)
        ResolveAllOf(SchemaNode schema)
    {
        string? baseClassName = null;
        var extraProperties = new List<PropertyNode>();
        var descriptions = new List<string>();

        foreach (var part in schema.AllOf)
        {
            if (!string.IsNullOrEmpty(part.Name) && part.Properties.Count == 0 && baseClassName is null)
            {
                baseClassName = part.Name;
                continue;
            }
            extraProperties.AddRange(part.Properties);
        }

        return (baseClassName, extraProperties, descriptions);
    }
}
