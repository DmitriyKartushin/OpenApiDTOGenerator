namespace OpenApiDtoGenerator.Generators.Validators;

/// <summary>
/// Опции генерации FluentValidation валидаторов.
/// </summary>
public sealed record DtoValidatorOptions(
    string Namespace,
    string ClassSuffix = "");
