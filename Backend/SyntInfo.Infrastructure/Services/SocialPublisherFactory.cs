using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Services;

public class SocialPublisherFactory : ISocialPublisherFactory
{
    private readonly IEnumerable<ISocialPlatformPublisher> _publishers;

    public SocialPublisherFactory(IEnumerable<ISocialPlatformPublisher> publishers)
    {
        _publishers = publishers;
    }

    public ISocialPlatformPublisher? GetPublisher(SocialPlatformType platformType)
    {
        return _publishers.FirstOrDefault(p => p.PlatformType == platformType);
    }
}
