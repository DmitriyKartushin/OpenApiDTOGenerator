using System.Text;
using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Generators.Dto;

namespace OpenApiDtoGenerator.Generators.Masking;

/// <summary>
/// Генератор системы маскировки чувствительных данных при JSON-сериализации.
///
/// Что генерирует:
///   1. Runtime-инфраструктуру (5 файлов):
///      - MaskStrategy enum
///      - MaskAttribute
///      - MaskingStrategyExecutor
///      - MaskingJsonConverter (JsonConverter&lt;string&gt;)
///      - MaskingJsonOptions.Default (готовый JsonSerializerOptions)
///
///   2. Partial-расширения DTO-классов:
///      - Метод ToMaskedJson()
///      - Файл {DtoName}.Masking.cs
///
///   3. Атрибуты на свойствах DTO (используется через GetMaskAttributeForProperty):
///      [Mask(MaskStrategy.Partial, 2, 2)]
/// </summary>
public sealed class MaskingGenerator
{
    /// <summary>
    /// Генерирует runtime-инфраструктуру (5 файлов).
    /// </summary>
    public List<GeneratedFile> GenerateInfrastructure(MaskingGeneratorOptions options)
    {
        return new List<GeneratedFile>
        {
            new("MaskStrategy.cs",             GenerateMaskStrategyFile(options)),
            new("MaskAttribute.cs",            GenerateMaskAttributeFile(options)),
            new("MaskingStrategyExecutor.cs",  GenerateStrategyExecutorFile(options)),
            new("MaskingJsonConverter.cs",     GenerateJsonConverterFile(options)),
            new("MaskingJsonOptions.cs",       GenerateJsonOptionsFile(options))
        };
    }

    /// <summary>
    /// Генерирует partial-расширение DTO с методом ToMaskedJson().
    /// Возвращает null, если у схемы нет ни одного маскируемого поля.
    /// </summary>
    public GeneratedFile? GenerateDtoExtension(SchemaNode schema, MaskingGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (string.IsNullOrEmpty(schema.Name))
            return null;

        // Проверяем: есть ли хоть одно свойство под маскировку
        var hasMaskedFields = schema.Properties.Any(p =>
            options.Config.FindRuleForField(p.Name) is not null);

        if (!hasMaskedFields)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine($"using {options.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"namespace {options.DtoNamespace};");
        sb.AppendLine();
        sb.AppendLine($"public partial class {schema.Name}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Сериализует объект в JSON с применением правил маскировки чувствительных полей.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public string ToMaskedJson()");
        sb.AppendLine("    {");
        sb.AppendLine("        return JsonSerializer.Serialize(this, MaskingJsonOptions.Default);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new GeneratedFile($"{schema.Name}.Masking.cs", sb.ToString());
    }

    /// <summary>
    /// Возвращает строку атрибута [Mask(...)] для указанного поля, если оно подпадает под маскировку.
    /// Использует DtoGenerator при генерации свойств.
    /// </summary>
    public string? GetMaskAttributeForProperty(string fieldName, MaskingConfig config)
    {
        var rule = config.FindRuleForField(fieldName);
        if (rule is null)
            return null;

        return rule.Strategy switch
        {
            MaskStrategy.Partial => $"[Mask(MaskStrategy.Partial, {rule.VisibleStart}, {rule.VisibleEnd})]",
            _                     => $"[Mask(MaskStrategy.{rule.Strategy})]"
        };
    }

    /// <summary>
    /// Генерирует всю систему: инфраструктуру + partial-расширения для каждой схемы.
    /// </summary>
    public List<GeneratedFile> GenerateAll(
        IReadOnlyDictionary<string, SchemaNode> schemas,
        MaskingGeneratorOptions options)
    {
        var files = GenerateInfrastructure(options);

        foreach (var schema in schemas.Values)
        {
            var ext = GenerateDtoExtension(schema, options);
            if (ext is not null)
                files.Add(ext);
        }

        return files;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Infrastructure file generators
    // ─────────────────────────────────────────────────────────────────────────

    private static string GenerateMaskStrategyFile(MaskingGeneratorOptions options) =>
        $$"""
        namespace {{options.Namespace}};

        /// <summary>
        /// Стратегии маскировки строковых значений.
        /// </summary>
        public enum MaskStrategy
        {
            /// <summary>Без маскировки.</summary>
            None,

            /// <summary>Всё значение → "***".</summary>
            Full,

            /// <summary>Видны первые VisibleStart и последние VisibleEnd символов.</summary>
            Partial,

            /// <summary>Только первый символ.</summary>
            FirstChar,

            /// <summary>Для дат: сохраняется только год.</summary>
            YearOnly,

            /// <summary>SHA-256 хеш значения.</summary>
            Hash
        }
        """;

    private static string GenerateMaskAttributeFile(MaskingGeneratorOptions options) =>
        $$"""
        namespace {{options.Namespace}};

        /// <summary>
        /// Атрибут на свойстве DTO, определяющий стратегию маскировки при вызове ToMaskedJson().
        /// </summary>
        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
        public sealed class MaskAttribute : Attribute
        {
            public MaskStrategy Strategy { get; }
            public int VisibleStart { get; }
            public int VisibleEnd { get; }

            public MaskAttribute(MaskStrategy strategy, int visibleStart = 0, int visibleEnd = 0)
            {
                Strategy = strategy;
                VisibleStart = visibleStart;
                VisibleEnd = visibleEnd;
            }
        }
        """;

    private static string GenerateStrategyExecutorFile(MaskingGeneratorOptions options) =>
        $$"""
        using System.Security.Cryptography;
        using System.Text;

        namespace {{options.Namespace}};

        /// <summary>
        /// Runtime-исполнитель стратегий маскировки.
        /// </summary>
        public sealed class MaskingStrategyExecutor
        {
            public string? Apply(string? value, MaskStrategy strategy, int visibleStart = 0, int visibleEnd = 0)
            {
                if (value is null)
                    return null;

                return strategy switch
                {
                    MaskStrategy.None      => value,
                    MaskStrategy.Full      => ApplyFull(value),
                    MaskStrategy.Partial   => ApplyPartial(value, visibleStart, visibleEnd),
                    MaskStrategy.FirstChar => ApplyFirstChar(value),
                    MaskStrategy.YearOnly  => ApplyYearOnly(value),
                    MaskStrategy.Hash      => ApplyHash(value),
                    _                       => value
                };
            }

            private static string ApplyFull(string value) => value.Length == 0 ? string.Empty : "***";

            private static string ApplyPartial(string value, int visibleStart, int visibleEnd)
            {
                if (value.Length == 0) return value;
                if (visibleStart + visibleEnd >= value.Length) return new string('*', value.Length);

                var start = value[..visibleStart];
                var end = value[^visibleEnd..];
                var middleLength = value.Length - visibleStart - visibleEnd;
                return start + new string('*', middleLength) + end;
            }

            private static string ApplyFirstChar(string value)
            {
                if (value.Length == 0) return string.Empty;
                if (value.Length == 1) return value;
                return value[0] + new string('*', value.Length - 1);
            }

            private static string ApplyYearOnly(string value)
            {
                if (value.Length >= 4 && value[4] == '-' && int.TryParse(value.AsSpan(0, 4), out _))
                    return $"{value[..4]}-**-**";
                return "***";
            }

            private static string ApplyHash(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                var hash = SHA256.HashData(bytes);
                return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
            }
        }
        """;

    private static string GenerateJsonConverterFile(MaskingGeneratorOptions options) =>
        $$"""
        using System.Reflection;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        namespace {{options.Namespace}};

        /// <summary>
        /// JsonConverter, применяющий маскировку к строковым свойствам,
        /// помеченным атрибутом [Mask(...)].
        ///
        /// Реализован как контекстный конвертер: при сериализации объекта
        /// проходит по свойствам через рефлексию и вручную выводит JSON.
        /// </summary>
        public sealed class MaskingJsonConverter<T> : JsonConverter<T> where T : class
        {
            private static readonly MaskingStrategyExecutor Executor = new();

            public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // Десериализация не маскируется — используем стандартный десериалайзер.
                return JsonSerializer.Deserialize<T>(ref reader, DefaultOptionsWithoutSelf(options));
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    var jsonName = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;
                    var raw = prop.GetValue(value);

                    var maskAttr = prop.GetCustomAttribute<MaskAttribute>();
                    if (maskAttr is not null && raw is string s)
                    {
                        var masked = Executor.Apply(s, maskAttr.Strategy, maskAttr.VisibleStart, maskAttr.VisibleEnd);
                        writer.WriteString(jsonName, masked);
                    }
                    else
                    {
                        writer.WritePropertyName(jsonName);
                        JsonSerializer.Serialize(writer, raw, prop.PropertyType, DefaultOptionsWithoutSelf(options));
                    }
                }
                writer.WriteEndObject();
            }

            private static JsonSerializerOptions DefaultOptionsWithoutSelf(JsonSerializerOptions options)
            {
                // Копия опций без нашего конвертера, чтобы избежать рекурсии.
                var copy = new JsonSerializerOptions(options);
                for (var i = copy.Converters.Count - 1; i >= 0; i--)
                {
                    if (copy.Converters[i].GetType() == GetType()) copy.Converters.RemoveAt(i);
                }
                return copy;
            }
        }
        """;

    private static string GenerateJsonOptionsFile(MaskingGeneratorOptions options) =>
        $$"""
        using System.Text.Json;

        namespace {{options.Namespace}};

        /// <summary>
        /// Готовые JsonSerializerOptions с настроенной маскировкой.
        /// Используется в ToMaskedJson() на каждом DTO.
        /// </summary>
        public static class MaskingJsonOptions
        {
            public static JsonSerializerOptions Default { get; } = new()
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }
        """;
}
