using SyntInfo.Domain.Entities;

namespace SyntInfo.UnitTests.Domain;

public class SocialPostDraftTests
{
    [Fact]
    public void Approve_FromDraft_SetsStatusApprovedAndClearsErrorMessage()
    {
        // Arrange
        var draft = new SocialPostDraft
        {
            Status = SocialPostStatus.Draft,
            ErrorMessage = "Poprzedni błąd"
        };

        // Act
        draft.Approve();

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Approved);
        draft.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Approve_FromFailed_SetsStatusApprovedAndClearsErrorMessage()
    {
        // Arrange
        var draft = new SocialPostDraft
        {
            Status = SocialPostStatus.Failed,
            ErrorMessage = "Błąd sieci"
        };

        // Act
        draft.Approve();

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Approved);
        draft.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(SocialPostStatus.Approved)]
    [InlineData(SocialPostStatus.Published)]
    [InlineData(SocialPostStatus.Rejected)]
    public void Approve_FromDisallowedStatus_ThrowsInvalidOperationException(SocialPostStatus initialStatus)
    {
        // Arrange
        var draft = new SocialPostDraft { Status = initialStatus };

        // Act
        var act = () => draft.Approve();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(SocialPostStatus.Draft)]
    [InlineData(SocialPostStatus.Failed)]
    public void Reject_FromAllowedStatus_SetsStatusRejected(SocialPostStatus initialStatus)
    {
        // Arrange
        var draft = new SocialPostDraft { Status = initialStatus };

        // Act
        draft.Reject();

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Rejected);
    }

    [Theory]
    [InlineData(SocialPostStatus.Approved)]
    [InlineData(SocialPostStatus.Published)]
    [InlineData(SocialPostStatus.Rejected)]
    public void Reject_FromDisallowedStatus_ThrowsInvalidOperationException(SocialPostStatus initialStatus)
    {
        // Arrange
        var draft = new SocialPostDraft { Status = initialStatus };

        // Act
        var act = () => draft.Reject();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkPublished_FromApproved_SetsPublishedStatusExternalIdAndDate()
    {
        // Arrange
        var draft = new SocialPostDraft
        {
            Status = SocialPostStatus.Approved,
            ErrorMessage = "Stary błąd"
        };
        var now = DateTime.UtcNow;

        // Act
        draft.MarkPublished("ext-123", now);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Published);
        draft.ExternalPostId.Should().Be("ext-123");
        draft.PublishedAt.Should().Be(now);
        draft.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(SocialPostStatus.Draft)]
    [InlineData(SocialPostStatus.Failed)]
    [InlineData(SocialPostStatus.Rejected)]
    [InlineData(SocialPostStatus.Published)]
    public void MarkPublished_WhenNotApproved_ThrowsInvalidOperationException(SocialPostStatus initialStatus)
    {
        // Arrange
        var draft = new SocialPostDraft { Status = initialStatus };

        // Act
        var act = () => draft.MarkPublished("ext-123", DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkFailed_WithLongErrorMessage_TruncatesTo2000Characters()
    {
        // Arrange
        var draft = new SocialPostDraft { Status = SocialPostStatus.Approved };
        var longError = new string('x', 2500);

        // Act
        draft.MarkFailed(longError);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Failed);
        draft.ErrorMessage.Should().HaveLength(2000);
    }
}

