using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Core.Parsing;

namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Главный оркестратор резолвинга $ref ссылок.
/// 
/// Координирует:
///   1. RefParser — разбор $ref на Kind, Source, JsonPointer
///   2. DocumentLoader — загрузка документа (локально или по сети)
///   3. DocumentCache — кеширование загруженных документов
///   4. IDocumentParser — парсинг YAML/JSON в дерево (Dictionary/List)
///   5. JsonPointerResolver — навигация по дереву документа
///   6. CycleDetector — обнаружение циклических ссылок
///
/// Возвращает SchemaNode — готовый к использованию IR узел.
/// </summary>
public sealed class RefResolver : IRefResolver
{
    private readonly IDocumentLoader _loader;
    private readonly DocumentCache _cache;
    private readonly IDocumentParser _documentParser;
    private readonly RefParser _refParser = new();
    private readonly JsonPointerResolver _pointerResolver = new();
    private readonly CycleDetector _cycleDetector = new();

    public RefResolver(IDocumentLoader loader, DocumentCache cache, IDocumentParser? documentParser = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        // По умолчанию — YamlDocumentParser (парсит и YAML, и JSON)
        _documentParser = documentParser ?? new YamlDocumentParser();
    }

    /// <summary>
    /// Резолвит $ref ссылку в SchemaNode.
    /// </summary>
    /// <param name="refString">Строка $ref (например "#/components/schemas/Foo" или "./schemas/foo.yaml#/Bar").</param>
    /// <param name="currentDocumentKey">Нормализованный ключ текущего документа (абс. путь или URI).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>SchemaNode, готовый к использованию.</returns>
    /// <exception cref="ArgumentException">Если refString или currentDocumentKey пустые/null.</exception>
    /// <exception cref="CyclicReferenceException">Обнаружена циклическая ссылка.</exception>
    /// <exception cref="FileNotFoundException">Внешний документ не найден.</exception>
    /// <exception cref="JsonPointerException">Путь в документе не существует.</exception>
    public async Task<SchemaNode> ResolveAsync(
        string refString,
        string currentDocumentKey,
        CancellationToken ct = default)
    {
        ValidateInputs(refString, currentDocumentKey);

        // ── 1. Используем CycleDetector.Scope для атомарного Enter/Leave ──
        using (_cycleDetector.Scope(refString))
        {
            return await ResolveInternalAsync(refString, currentDocumentKey, ct);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core logic
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<SchemaNode> ResolveInternalAsync(
        string refString,
        string currentDocumentKey,
        CancellationToken ct)
    {
        // ── 2. Разбираем $ref ──────────────────────────────────────────────
        var parsedRef = _refParser.Parse(refString);

        // ── 3. Определяем ключ целевого документа ──────────────────────────
        var targetDocKey = GetTargetDocumentKey(parsedRef, currentDocumentKey);

        // ── 4. Загружаем/получаем из кеша целевой документ ─────────────────
        var cachedDoc = await _cache.GetOrAddAsync(
            targetDocKey,
            async key =>
            {
                var rawContent = await _loader.LoadAsync(key, ct);
                var format = key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? DocumentFormat.Json
                    : DocumentFormat.Yaml;
                return new CachedDocument(rawContent, format);
            },
            ct);

        // ── 5. Парсим документ в дерево (Dictionary/List) через IDocumentParser ──
        var documentTree = _documentParser.Parse(cachedDoc.RawContent);

        // ── 6. Навигируем по JSON Pointer (если есть) ──────────────────────
        var schemaObject = GetSchemaObjectAtPointer(documentTree, parsedRef.JsonPointer);

        // ── 7. Преобразуем в SchemaNode (IR) ───────────────────────────────
        var schemaNode = ConvertToSchemaNode(schemaObject, refString);

        return schemaNode;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Определяет нормализованный ключ целевого документа.
    /// </summary>
    public static string GetTargetDocumentKey(ParsedRef parsedRef, string currentDocumentKey)
    {
        // Локальная ссылка — целевой документ совпадает с текущим
        if (parsedRef.IsLocal)
            return currentDocumentKey;

        // Внешняя ссылка — используем Source как есть или нормализуем относительный путь
        var source = parsedRef.Source!;

        // Абсолютный путь или URI — используем как есть
        if (parsedRef.Kind == RefKind.AbsoluteFile || parsedRef.Kind == RefKind.Remote)
            return source;

        // Относительный файл — нормализуем относительно текущего документа
        if (parsedRef.Kind == RefKind.RelativeFile)
        {
            var currentDir = Path.GetDirectoryName(currentDocumentKey)
                ?? throw new InvalidOperationException($"Cannot determine directory of '{currentDocumentKey}'");

            var absolutePath = Path.GetFullPath(Path.Combine(currentDir, source));
            return absolutePath;
        }

        throw new InvalidOperationException($"Unknown RefKind: {parsedRef.Kind}");
    }

    /// <summary>
    /// Применяет JSON Pointer для получения узла из дерева.
    /// Если pointer пуст/null — возвращает корень документа.
    /// </summary>
    private object? GetSchemaObjectAtPointer(object? documentTree, string? jsonPointer)
    {
        // Пустой pointer — вернуть весь документ
        if (string.IsNullOrEmpty(jsonPointer))
            return documentTree;

        // Применить JSON Pointer для навигации
        return _pointerResolver.Resolve(documentTree, jsonPointer);
    }

    /// <summary>
    /// Преобразует объект из дерева документа в SchemaNode (IR).
    /// На этом этапе это может быть неполный узел (детали будут резолвлены позже).
    /// </summary>
    private static SchemaNode ConvertToSchemaNode(object? schemaObject, string originalRef)
    {
        // Если это скаляр — ошибка
        if (schemaObject is not Dictionary<string, object?> schemaDict)
            throw new InvalidOperationException(
                $"$ref '{originalRef}' points to a non-object value. Expected an object/schema.");

        // Пытаемся извлечь имя схемы из $ref (последний сегмент пути)
        var name = originalRef.Contains('/')
            ? originalRef.Split('/').LastOrDefault()
            : null;

        // Простое преобразование (детали реализуются в IRBuilder)
        var node = new SchemaNode
        {
            Name = name,
            OriginalRef = originalRef,
            Type = ExtractType(schemaDict),
            Description = ExtractDescription(schemaDict),
            // Остальные поля заполняются в IRBuilder при полном разборе документа
        };

        return node;
    }

    private static string? ExtractType(Dictionary<string, object?> schema)
    {
        return schema.TryGetValue("type", out var typeObj)
            ? typeObj as string
            : null;
    }

    private static string? ExtractDescription(Dictionary<string, object?> schema)
    {
        return schema.TryGetValue("description", out var descObj)
            ? descObj as string
            : null;
    }

    private static void ValidateInputs(string refString, string currentDocumentKey)
    {
        if (string.IsNullOrWhiteSpace(refString))
            throw new ArgumentException("$ref string must not be null or whitespace.", nameof(refString));

        if (string.IsNullOrWhiteSpace(currentDocumentKey))
            throw new ArgumentException("Current document key must not be null or whitespace.", nameof(currentDocumentKey));
    }
}
