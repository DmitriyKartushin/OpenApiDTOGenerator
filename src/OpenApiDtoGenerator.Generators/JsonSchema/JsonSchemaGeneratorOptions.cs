namespace OpenApiDtoGenerator.Generators.JsonSchema;

/// <summary>
/// Опции генерации JSON Schema для тел входящих запросов.
/// </summary>
/// <param name="HttpMethods">
///   HTTP-методы, для тел запросов которых генерируются схемы (по умолчанию — только POST).
/// </param>
/// <param name="ExcludeReadOnlyFromRequired">
///   Убирать readOnly-свойства из required для запросов: по спецификации OpenAPI клиент их не передаёт.
///   Для ответов не применяется: readOnly-свойства сервер возвращает.
/// </param>
/// <param name="IncludeResponses">
///   Дополнительно генерировать схемы тел ответов (файлы *.Rs.&lt;код&gt;.schema.json).
/// </param>
public sealed record JsonSchemaGeneratorOptions(
    IReadOnlyList<string>? HttpMethods = null,
    bool ExcludeReadOnlyFromRequired = true,
    bool IncludeResponses = false)
{
    public IReadOnlyList<string> EffectiveHttpMethods => HttpMethods ?? ["post"];
}
