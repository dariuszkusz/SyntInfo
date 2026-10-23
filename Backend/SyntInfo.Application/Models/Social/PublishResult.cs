namespace SyntInfo.Application.Models.Social;

public record PublishResult(
    bool Success,
    string? ExternalPostId = null,
    string? ErrorMessage = null)
{
    public static PublishResult Succeeded(string externalPostId) => new(true, externalPostId);
    public static PublishResult Failed(string errorMessage) => new(false, null, errorMessage);
}
