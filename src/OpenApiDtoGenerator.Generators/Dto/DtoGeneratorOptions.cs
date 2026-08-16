using OpenApiDtoGenerator.Generators.Masking;

namespace OpenApiDtoGenerator.Generators.Dto;

/// <summary>
/// Опции генерации DTO классов.
/// </summary>
/// <param name="Namespace">Namespace для сгенерированных классов.</param>
/// <param name="ClassPrefix">Префикс имени класса (например "Dto" → DtoPersonInfo).</param>
/// <param name="ClassSuffix">Суффикс имени класса (например "Dto" → PersonInfoDto).</param>
/// <param name="GeneratePartial">Генерировать классы как partial (для расширения masking/validation).</param>
/// <param name="MaskingConfig">Опциональная конфигурация маскировки — добавит [Mask] атрибуты и using.</param>
/// <param name="MaskingNamespace">Namespace masking-инфраструктуры (обязателен если MaskingConfig задан).</param>
public sealed record DtoGeneratorOptions(
    string Namespace,
    string ClassPrefix = "",
    string ClassSuffix = "",
    bool GeneratePartial = false,
    MaskingConfig? MaskingConfig = null,
    string? MaskingNamespace = null);
