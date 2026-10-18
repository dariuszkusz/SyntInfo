namespace SyntInfo.Application.Interfaces;

public interface IHejtoClient
{
    /// <summary>
    /// Tworzy wpis na Hejto (POST /posts) w wybranej społeczności.
    /// </summary>
    Task<string> CreatePostAsync(string content, string? communitySlug = null, CancellationToken cancellationToken = default);
}
