using SyntInfo.Domain.Entities;

namespace SyntInfo.Application.Interfaces;

public interface ISocialPublisherFactory
{
    ISocialPlatformPublisher? GetPublisher(SocialPlatformType platformType);
}
