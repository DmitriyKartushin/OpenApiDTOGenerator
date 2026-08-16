namespace OpenApiDtoGenerator.Generators.Masking;

/// <summary>
/// Опции генерации masking-подсистемы.
/// </summary>
/// <param name="Namespace">Namespace для runtime-инфраструктуры (MaskStrategy, MaskAttribute...).</param>
/// <param name="DtoNamespace">Namespace DTO-классов (для partial-расширений).</param>
/// <param name="Config">Конфигурация маскировки: правила по именам полей.</param>
public sealed record MaskingGeneratorOptions(
    string Namespace,
    string DtoNamespace,
    MaskingConfig Config);
