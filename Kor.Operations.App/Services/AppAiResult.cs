#nullable enable

namespace Kor.Operations.Services;

internal sealed record AppAiResult(bool IsSuccess, string Text, string? ErrorMessage)
{
    public static AppAiResult Success(string text) => new(true, text ?? string.Empty, null);

    public static AppAiResult Failure(string message) => new(false, string.Empty, message);
}
