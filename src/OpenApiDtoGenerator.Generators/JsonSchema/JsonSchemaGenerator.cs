using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using OpenApiDtoGenerator.Core.Parsing;
using OpenApiDtoGenerator.Core.Resolving;
using OpenApiDtoGenerator.Generators.Dto;

namespace OpenApiDtoGenerator.Generators.JsonSchema;

/// <summary>
/// Генерирует JSON Schema (draft 2020-12) для тел входящих запросов из OpenAPI 3.x.
///
/// Работает по сырому дереву документа, а не по IR: для валидации нужны все
/// ключевые слова схемы, включая вложенные inline-объекты и $ref-цепочки.
///
/// Для каждой операции (по умолчанию POST) с JSON requestBody создаётся
/// самодостаточный файл &lt;operationId&gt;.Rq.schema.json. При включённых ответах
/// (IncludeResponses) для каждого JSON-ответа создаётся &lt;operationId&gt;.Rs.&lt;код&gt;.schema.json:
///   - все $ref (локальные и внешние) переносятся в $defs → "#/$defs/Name"
///   - nullable: true → type: [T, "null"] (или anyOf с {type: null})
///   - exclusiveMinimum/exclusiveMaximum: true → числовая форма draft 2020-12
///   - example → examples: [...]
///   - discriminator, xml, externalDocs, x-* → удаляются (не ключевые слова JSON Schema)
///   - readOnly-свойства исключаются из required (клиент их не передаёт)
///
/// Дополнительно генерируется index.json — маппинг "метод + путь → файл схемы".
/// </summary>
public sealed class JsonSchemaGenerator
{
    public const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";
    public const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Кириллица в description/pattern остаётся читаемой
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Явный resolver — работает и при отключённой reflection-сериализации
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly IDocumentLoader _loader;
    private readonly IDocumentParser _parser;
    private readonly RefParser _refParser = new();
    private readonly JsonPointerResolver _pointerResolver = new();

    public JsonSchemaGenerator(IDocumentLoader? loader = null, IDocumentParser? parser = null)
    {
        _loader = loader ?? new LocalFileLoader();
        _parser = parser ?? new YamlDocumentParser();
    }

    /// <summary>
    /// Генерирует JSON Schema файлы для всех подходящих операций + index.json.
    /// </summary>
    /// <param name="documentTree">Распарсенное дерево OpenAPI документа.</param>
    /// <param name="documentKey">Ключ документа (абс. путь) — для относительных $ref.</param>
    /// <param name="options">Опции генерации.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task<List<GeneratedFile>> GenerateAllAsync(
        object? documentTree,
        string documentKey,
        JsonSchemaGeneratorOptions options,
        CancellationToken ct = default)
    {
        if (documentTree is not Dictionary<string, object?> doc)
            throw new ArgumentException("Document tree must be a Dictionary.", nameof(documentTree));
        ArgumentNullException.ThrowIfNull(options);

        var store = new DocumentStore(this, documentKey, documentTree);
        var results = new List<GeneratedFile>();
        var index = new JsonArray();
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!doc.TryGetValue("paths", out var pathsObj) || pathsObj is not Dictionary<string, object?> paths)
            return results;

        foreach (var (path, pathItemObj) in paths)
        {
            if (pathItemObj is not Dictionary<string, object?> pathItem)
                continue;

            foreach (var method in options.EffectiveHttpMethods.Select(m => m.ToLowerInvariant()))
            {
                if (!pathItem.TryGetValue(method, out var opObj) || opObj is not Dictionary<string, object?> operation)
                    continue;

                var operationId = operation.TryGetValue("operationId", out var opIdObj) ? opIdObj as string : null;
                var op = new OperationInfo(
                    method.ToUpperInvariant(),
                    path,
                    operationId,
                    SanitizeFileName(operationId ?? $"{method}{path}"));

                if (operation.TryGetValue("requestBody", out var requestBodyObj) && requestBodyObj is not null)
                {
                    var (requestBody, bodyDocKey) = await ResolveRefChainAsync(requestBodyObj, documentKey, store, ct);
                    if (requestBody is not null)
                    {
                        var target = new BodyTarget(op, BodyKind.Request, StatusCode: null,
                            $"Request body of {op.Method} {path}" +
                            (operationId is not null ? $" (operationId: {operationId})" : string.Empty));
                        await AddBodyFilesAsync(requestBody, bodyDocKey, target, options, store, results, index, usedFileNames, ct);
                    }
                }

                if (options.IncludeResponses &&
                    operation.TryGetValue("responses", out var responsesObj) &&
                    responsesObj is Dictionary<string, object?> responses)
                {
                    foreach (var (statusCode, responseObj) in responses)
                    {
                        var (response, responseDocKey) = await ResolveRefChainAsync(responseObj, documentKey, store, ct);
                        if (response is null)
                            continue;

                        var target = new BodyTarget(op, BodyKind.Response, statusCode,
                            $"Response {statusCode} of {op.Method} {path}" +
                            (operationId is not null ? $" (operationId: {operationId})" : string.Empty));
                        await AddBodyFilesAsync(response, responseDocKey, target, options, store, results, index, usedFileNames, ct);
                    }
                }
            }
        }

        if (results.Count > 0)
            results.Add(new GeneratedFile(IndexFileName, index.ToJsonString(SerializerOptions)));

        return results;
    }

    private async Task AddBodyFilesAsync(
        Dictionary<string, object?> body,
        string bodyDocKey,
        BodyTarget target,
        JsonSchemaGeneratorOptions options,
        DocumentStore store,
        List<GeneratedFile> results,
        JsonArray index,
        HashSet<string> usedFileNames,
        CancellationToken ct)
    {
        if (!body.TryGetValue("content", out var contentObj) || contentObj is not Dictionary<string, object?> content)
            return;

        var jsonMediaTypes = content
            .Where(c => IsJsonMediaType(c.Key) && c.Value is Dictionary<string, object?> m && m.ContainsKey("schema"))
            .ToList();

        var isRequest = target.Kind == BodyKind.Request;
        var schemaOptions = isRequest ? options : options with { ExcludeReadOnlyFromRequired = false };
        var required = isRequest && body.TryGetValue("required", out var req) && req is true;

        foreach (var (mediaType, mediaObj) in jsonMediaTypes)
        {
            var schemaObj = ((Dictionary<string, object?>)mediaObj!)["schema"];
            var comment = $"{target.Description}, content type {mediaType}";

            var schemaDocument = await BuildSchemaDocumentAsync(schemaObj, bodyDocKey, store, schemaOptions, comment, ct);

            var kindPart = isRequest ? "Rq" : $"Rs.{SanitizeFileName(target.StatusCode!)}";
            var fileBase = $"{target.Operation.BaseName}.{kindPart}";
            if (jsonMediaTypes.Count > 1)
                fileBase += $".{SanitizeFileName(mediaType)}";
            var fileName = ReserveUniqueName($"{fileBase}.schema.json", usedFileNames);

            results.Add(new GeneratedFile(fileName, schemaDocument.ToJsonString(SerializerOptions)));

            index.Add(new JsonObject
            {
                ["method"] = target.Operation.Method,
                ["path"] = target.Operation.Path,
                ["operationId"] = target.Operation.OperationId,
                ["kind"] = isRequest ? "Rq" : "Rs",
                ["statusCode"] = target.StatusCode,
                ["contentType"] = mediaType,
                ["required"] = isRequest ? required : null,
                ["schemaFile"] = fileName
            });
        }
    }

    /// <summary>
    /// Строит самодостаточный JSON Schema документ из одной OpenAPI схемы.
    /// </summary>
    /// <param name="schema">OpenAPI схема (узел дерева документа).</param>
    /// <param name="documentTree">Дерево документа, в котором находится схема (для локальных $ref).</param>
    /// <param name="documentKey">Ключ документа (абс. путь) — для относительных $ref.</param>
    /// <param name="options">Опции генерации.</param>
    /// <param name="ct">Токен отмены.</param>
    public Task<JsonObject> BuildSchemaDocumentAsync(
        object? schema,
        object? documentTree,
        string documentKey,
        JsonSchemaGeneratorOptions options,
        CancellationToken ct = default)
    {
        var store = new DocumentStore(this, documentKey, documentTree);
        return BuildSchemaDocumentAsync(schema, documentKey, store, options, comment: null, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core conversion
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<JsonObject> BuildSchemaDocumentAsync(
        object? schema,
        string documentKey,
        DocumentStore store,
        JsonSchemaGeneratorOptions options,
        string? comment,
        CancellationToken ct)
    {
        var ctx = new ConversionContext(store, options);
        var root = await ConvertSchemaAsync(schema, documentKey, ctx, ct);

        // ── Дотягиваем все схемы, на которые были ссылки (включая транзитивные) ──
        var defs = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        while (ctx.Pending.TryDequeue(out var pending))
            defs[pending.DefName] = await ConvertSchemaAsync(pending.Node, pending.DocumentKey, ctx, ct);

        var document = new JsonObject { ["$schema"] = SchemaDialect };
        if (comment is not null)
            document["$comment"] = comment;

        if (root is JsonObject rootObject)
        {
            foreach (var (key, value) in rootObject.ToList())
            {
                rootObject.Remove(key);
                document[key] = value;
            }
        }
        else
        {
            // Булева схема (true/false) — допустима в OpenAPI 3.1
            document["allOf"] = new JsonArray(root);
        }

        if (defs.Count > 0)
        {
            var defsObject = new JsonObject();
            foreach (var (name, def) in defs)
                defsObject[name] = def;
            document["$defs"] = defsObject;
        }

        return document;
    }

    private async Task<JsonNode?> ConvertSchemaAsync(
        object? node,
        string documentKey,
        ConversionContext ctx,
        CancellationToken ct)
    {
        if (node is not Dictionary<string, object?> schema)
            return ToJsonNode(node);

        // В OpenAPI 3.0 соседи $ref игнорируются — оставляем только ссылку
        if (schema.TryGetValue("$ref", out var refObj) && refObj is string refString)
            return new JsonObject { ["$ref"] = await RegisterDefAsync(refString, documentKey, ctx, ct) };

        var result = new JsonObject();

        foreach (var (key, value) in schema)
        {
            switch (key)
            {
                // ── Подсхемы-словари: { name → schema } ─────────────────────
                case "properties" or "patternProperties" or "dependentSchemas"
                    when value is Dictionary<string, object?> map:
                    var mapJson = new JsonObject();
                    foreach (var (name, sub) in map)
                        mapJson[name] = await ConvertSchemaAsync(sub, documentKey, ctx, ct);
                    result[key] = mapJson;
                    break;

                // ── Одиночные подсхемы ──────────────────────────────────────
                case "items" or "additionalProperties" or "not" or "propertyNames" or "contains"
                    when value is Dictionary<string, object?>:
                    result[key] = await ConvertSchemaAsync(value, documentKey, ctx, ct);
                    break;

                // ── Списки подсхем ──────────────────────────────────────────
                case "allOf" or "oneOf" or "anyOf" or "prefixItems" when value is List<object?> list:
                    var listJson = new JsonArray();
                    foreach (var sub in list)
                        listJson.Add(await ConvertSchemaAsync(sub, documentKey, ctx, ct));
                    result[key] = listJson;
                    break;

                case "required" when value is List<object?> requiredList:
                    var required = FilterRequired(schema, requiredList, ctx.Options);
                    if (required.Count > 0)
                        result[key] = required;
                    break;

                case "example" when !schema.ContainsKey("examples"):
                    result["examples"] = new JsonArray(ToJsonNode(value));
                    break;

                // ── Обрабатываются ниже / не являются ключевыми словами JSON Schema ──
                case "example" or "nullable" or "discriminator" or "xml" or "externalDocs":
                case "exclusiveMinimum" or "exclusiveMaximum" when value is bool:
                    break;

                default:
                    if (!key.StartsWith("x-", StringComparison.Ordinal))
                        result[key] = ToJsonNode(value);
                    break;
            }
        }

        ApplyExclusiveBound(schema, result, "minimum", "exclusiveMinimum");
        ApplyExclusiveBound(schema, result, "maximum", "exclusiveMaximum");

        return schema.TryGetValue("nullable", out var nullable) && nullable is true
            ? MakeNullable(result)
            : result;
    }

    /// <summary>
    /// Регистрирует цель $ref в $defs (один раз на цель) и возвращает ссылку на неё.
    /// Сама конвертация откладывается в очередь — это делает циклические ссылки безопасными.
    /// </summary>
    private async Task<string> RegisterDefAsync(
        string refString,
        string documentKey,
        ConversionContext ctx,
        CancellationToken ct)
    {
        var parsedRef = _refParser.Parse(refString);
        var targetKey = RefResolver.GetTargetDocumentKey(parsedRef, documentKey);
        var pointer = parsedRef.JsonPointer ?? string.Empty;
        var refKey = $"{targetKey}#{pointer}";

        if (!ctx.DefNames.TryGetValue(refKey, out var defName))
        {
            var tree = await ctx.Store.GetTreeAsync(targetKey, ct);
            var target = pointer.Length == 0 ? tree : _pointerResolver.Resolve(tree, pointer);

            defName = ctx.ReserveDefName(BuildDefBaseName(pointer, targetKey));
            ctx.DefNames[refKey] = defName;
            ctx.Pending.Enqueue(new PendingDef(defName, target, targetKey));
        }

        return $"#/$defs/{defName}";
    }

    /// <summary>
    /// Разворачивает цепочку $ref для не-схемных объектов (например requestBody →
    /// components/requestBodies/X). Возвращает объект и ключ документа, где он лежит.
    /// </summary>
    private async Task<(Dictionary<string, object?>? node, string documentKey)> ResolveRefChainAsync(
        object? node,
        string documentKey,
        DocumentStore store,
        CancellationToken ct)
    {
        var visited = new List<string>();

        while (true)
        {
            if (node is not Dictionary<string, object?> dict)
                return (null, documentKey);

            if (!dict.TryGetValue("$ref", out var refObj) || refObj is not string refString)
                return (dict, documentKey);

            var parsedRef = _refParser.Parse(refString);
            documentKey = RefResolver.GetTargetDocumentKey(parsedRef, documentKey);

            var refKey = $"{documentKey}#{parsedRef.JsonPointer}";
            if (visited.Contains(refKey))
                throw new CyclicReferenceException(refKey, visited);
            visited.Add(refKey);

            var tree = await store.GetTreeAsync(documentKey, ct);
            node = string.IsNullOrEmpty(parsedRef.JsonPointer)
                ? tree
                : _pointerResolver.Resolve(tree, parsedRef.JsonPointer);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OpenAPI → JSON Schema keyword helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// OpenAPI 3.0: exclusiveMinimum: true + minimum: N → draft 2020-12: exclusiveMinimum: N.
    /// </summary>
    private static void ApplyExclusiveBound(
        Dictionary<string, object?> schema,
        JsonObject result,
        string boundKey,
        string exclusiveKey)
    {
        if (!schema.TryGetValue(exclusiveKey, out var exclusive) || exclusive is not true)
            return;

        if (!result.TryGetPropertyValue(boundKey, out var bound))
            return;

        result.Remove(boundKey);
        result[exclusiveKey] = bound;
    }

    /// <summary>
    /// OpenAPI 3.0 nullable → JSON Schema: добавляем "null" в type (и в enum),
    /// а если type не задан (allOf/$ref/...) — оборачиваем в anyOf с {type: null}.
    /// </summary>
    private static JsonNode MakeNullable(JsonObject schema)
    {
        if (schema["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type))
        {
            schema["type"] = new JsonArray(type, "null");

            if (schema["enum"] is JsonArray enumValues && !enumValues.Any(v => v is null))
                enumValues.Add(null);

            return schema;
        }

        if (schema["type"] is JsonArray types)
        {
            if (!types.Any(t => t?.GetValue<string>() == "null"))
                types.Add("null");
            return schema;
        }

        return new JsonObject
        {
            ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" })
        };
    }

    private static JsonArray FilterRequired(
        Dictionary<string, object?> schema,
        List<object?> requiredList,
        JsonSchemaGeneratorOptions options)
    {
        var properties = schema.TryGetValue("properties", out var p) ? p as Dictionary<string, object?> : null;
        var result = new JsonArray();

        foreach (var name in requiredList.OfType<string>())
        {
            var isReadOnly = properties is not null &&
                             properties.TryGetValue(name, out var propObj) &&
                             propObj is Dictionary<string, object?> prop &&
                             prop.TryGetValue("readOnly", out var ro) && ro is true;

            if (options.ExcludeReadOnlyFromRequired && isReadOnly)
                continue;

            result.Add(name);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Преобразует узел дерева документа (Dictionary/List/скаляр) в JsonNode.
    /// </summary>
    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        Dictionary<string, object?> dict => new JsonObject(
            dict.Select(kv => KeyValuePair.Create(kv.Key, ToJsonNode(kv.Value)))),
        List<object?> list => new JsonArray(list.Select(ToJsonNode).ToArray()),
        _ => JsonValue.Create(value.ToString())
    };

    private static bool IsJsonMediaType(string mediaType)
    {
        var type = mediaType.Split(';')[0].Trim();
        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               type.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Имя для $defs: последний сегмент JSON Pointer'а
    /// ("#/components/schemas/PersonInfo" → "PersonInfo"),
    /// для ссылки на весь внешний файл — имя файла без расширения.
    /// </summary>
    private static string BuildDefBaseName(string pointer, string targetKey)
    {
        var rawName = pointer.Length > 0
            ? pointer.Split('/').Last().Replace("~1", "/").Replace("~0", "~")
            : Path.GetFileNameWithoutExtension(targetKey);

        var name = SanitizeIdentifier(rawName);
        return name.Length > 0 ? name : "Schema";
    }

    /// <summary>
    /// Оставляет только символы, безопасные и для имени файла, и для JSON Pointer в $ref.
    /// Недопустимые символы заменяются на '_', повторы схлопываются ("/items/{id}" → "items_id").
    /// </summary>
    private static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var safe = char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_';
            if (safe == '_' && sb.Length > 0 && sb[^1] == '_')
                continue;
            sb.Append(safe);
        }
        return sb.ToString().Trim('_');
    }

    private static string SanitizeFileName(string value)
    {
        var name = SanitizeIdentifier(value);
        return name.Length > 0 ? name : "request";
    }

    private static string ReserveUniqueName(string fileName, HashSet<string> used)
    {
        if (used.Add(fileName))
            return fileName;

        const string suffix = ".schema.json";
        var stem = fileName[..^suffix.Length];
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem}_{i}{suffix}";
            if (used.Add(candidate))
                return candidate;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // State
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record PendingDef(string DefName, object? Node, string DocumentKey);

    private sealed record OperationInfo(string Method, string Path, string? OperationId, string BaseName);

    private enum BodyKind { Request, Response }

    private sealed record BodyTarget(OperationInfo Operation, BodyKind Kind, string? StatusCode, string Description);

    /// <summary>
    /// Распарсенные деревья документов по ключу — общие для всех файлов одного запуска.
    /// </summary>
    private sealed class DocumentStore
    {
        private readonly JsonSchemaGenerator _owner;
        private readonly Dictionary<string, object?> _trees = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public DocumentStore(JsonSchemaGenerator owner, string rootKey, object? rootTree)
        {
            _owner = owner;
            _trees[rootKey] = rootTree;
        }

        public async Task<object?> GetTreeAsync(string documentKey, CancellationToken ct)
        {
            if (_trees.TryGetValue(documentKey, out var tree))
                return tree;

            var rawContent = await _owner._loader.LoadAsync(documentKey, ct);
            tree = _owner._parser.Parse(rawContent);
            _trees[documentKey] = tree;
            return tree;
        }
    }

    /// <summary>
    /// Состояние построения одного JSON Schema документа: реестр $defs и очередь конвертации.
    /// </summary>
    private sealed class ConversionContext(DocumentStore store, JsonSchemaGeneratorOptions options)
    {
        private readonly HashSet<string> _usedDefNames = new(StringComparer.Ordinal);

        public DocumentStore Store { get; } = store;
        public JsonSchemaGeneratorOptions Options { get; } = options;

        /// <summary>"docKey#pointer" → имя в $defs.</summary>
        public Dictionary<string, string> DefNames { get; } = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public Queue<PendingDef> Pending { get; } = new();

        /// <summary>
        /// Резервирует уникальное имя: одноимённые схемы из разных файлов получат суффикс _2, _3...
        /// </summary>
        public string ReserveDefName(string baseName)
        {
            if (_usedDefNames.Add(baseName))
                return baseName;

            for (var i = 2; ; i++)
            {
                var candidate = $"{baseName}_{i}";
                if (_usedDefNames.Add(candidate))
                    return candidate;
            }
        }
    }
}
