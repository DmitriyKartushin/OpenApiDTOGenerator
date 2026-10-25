namespace OpenApiDtoGenerator.Generators.JsonSchema;

/// <summary>
/// Опции генерации JSON Schema для тел входящих запросов.
/// </summary>
/// <param name="HttpMethods">
///   HTTP-методы, для тел запросов которых генерируются схемы (по умолчанию — только POST).
/// </param>
/// <param name="ExcludeReadOnlyFromRequired">
///   Убирать readOnly-свойства из required: по спецификации OpenAPI клиент их не передаёт.
/// </param>
public sealed record JsonSchemaGeneratorOptions(
    IReadOnlyList<string>? HttpMethods = null,
    bool ExcludeReadOnlyFromRequired = true)
{
    public IReadOnlyList<string> EffectiveHttpMethods => HttpMethods ?? ["post"];
}
