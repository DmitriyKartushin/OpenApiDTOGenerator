namespace OpenApiDtoGenerator.Core.Resolving;

/// <summary>
/// Исключение при невозможности разрешить JSON Pointer.
/// Содержит полный путь и токен, на котором произошла ошибка.
/// </summary>
public sealed class JsonPointerException : Exception
{
    /// <summary>Полный JSON Pointer, который не удалось разрешить.</summary>
    public string Pointer { get; }

    /// <summary>Токен (сегмент пути), на котором возникла ошибка.</summary>
    public string FailedToken { get; }

    public JsonPointerException(string pointer, string failedToken, string message)
        : base($"JSON Pointer '{pointer}' failed at token '{failedToken}': {message}")
    {
        Pointer = pointer;
        FailedToken = failedToken;
    }
}
