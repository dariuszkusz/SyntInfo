namespace SyntInfo.Application.Interfaces;

public interface IFacebookClient
{
    /// <summary>
    /// Publikuje wpis na stronie Facebook (Meta Graph API: POST /{page-id}/feed).
    /// </summary>
    Task<string> PublishPagePostAsync(string message, string? link = null, CancellationToken cancellationToken = default);
}
