namespace SyntInfo.Application.Interfaces;

public interface IHejtoClient
{
    /// <summary>
    /// Przesyła obrazek na serwer Hejto (POST /uploads?target=post&amp;source=file) i zwraca jego UUID.
    /// </summary>
    Task<string?> UploadImageAsync(string imageUrlOrPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tworzy wpis na Hejto (POST /posts) w wybranej społeczności, opcjonalnie z załączonym obrazkiem (imageUuid).
    /// </summary>
    Task<string> CreatePostAsync(
        string content,
        string? communitySlug = null,
        string? imageUuid = null,
        CancellationToken cancellationToken = default);
}
