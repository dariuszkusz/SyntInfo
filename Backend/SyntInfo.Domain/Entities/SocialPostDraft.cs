namespace SyntInfo.Domain.Entities;

public enum SocialPlatformType
{
    Wykop,
    Hejto,
    Facebook,
    Newsletter
}

public enum SocialPostStatus
{
    Draft,
    Approved,
    Published,
    Failed,
    Rejected
}

/// <summary>
/// Szkic posta na zewnętrzną platformę, oczekujący na akceptację administratora (human-in-the-loop).
/// Przejścia stanów: Draft -> Approved -> Published | Failed; Draft -> Rejected; Failed -> Approved (ponowienie).
/// </summary>
public class SocialPostDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NewsArticleId { get; set; }
    public NewsArticle? NewsArticle { get; set; }

    public SocialPlatformType PlatformType { get; set; }
    public string Content { get; set; } = string.Empty;
    public SocialPostStatus Status { get; set; } = SocialPostStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Identyfikator zewnętrzny opublikowanego wpisu (np. id posta na Facebooku).</summary>
    public string? ExternalPostId { get; set; }

    /// <summary>Id wiadomości Telegram z przyciskami — pozwala zaktualizować ją po decyzji administratora.</summary>
    public long? TelegramMessageId { get; set; }

    public bool CanBeApproved => Status is SocialPostStatus.Draft or SocialPostStatus.Failed;
    public bool CanBeRejected => Status is SocialPostStatus.Draft or SocialPostStatus.Failed;

    public void Approve()
    {
        if (!CanBeApproved)
            throw new InvalidOperationException($"Nie można zatwierdzić szkicu w statusie {Status}.");

        Status = SocialPostStatus.Approved;
        ErrorMessage = null;
    }

    public void Reject()
    {
        if (!CanBeRejected)
            throw new InvalidOperationException($"Nie można odrzucić szkicu w statusie {Status}.");

        Status = SocialPostStatus.Rejected;
    }

    public void MarkPublished(string? externalPostId, DateTime publishedAtUtc)
    {
        if (Status != SocialPostStatus.Approved)
            throw new InvalidOperationException($"Nie można oznaczyć jako opublikowany szkicu w statusie {Status}.");

        Status = SocialPostStatus.Published;
        ExternalPostId = externalPostId;
        PublishedAt = publishedAtUtc;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = SocialPostStatus.Failed;
        ErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
    }
}
