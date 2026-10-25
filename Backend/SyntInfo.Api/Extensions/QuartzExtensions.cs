using Quartz;
using SyntInfo.Infrastructure.BackgroundJobs;

namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection dla konfiguracji harmonogramu zadań w tle Quartz .NET.
/// </summary>
public static class QuartzExtensions
{
    /// <summary>
    /// Konfiguruje harmonogram zadań w tle Quartz wraz z jobami RssFetcherJob i SocialCurationJob.
    /// </summary>
    public static IServiceCollection AddQuartzBackgroundJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("RssFetcherJob");
            q.AddJob<RssFetcherJob>(opts => opts.WithIdentity(jobKey));

            var warsawTimeZone = GetWarsawTimeZone();

            // Odpalanie 2 razy dziennie (7:00 i 19:00 czasu polskiego)
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity("RssFetcherJob-trigger")
                .WithCronSchedule("0 0 7,19 * * ?", x => x.InTimeZone(warsawTimeZone)));

            // Odpalanie publikacji/selekcji na platformy społecznościowe raz dziennie o 8:00 (czasu polskiego)
            var socialJobKey = new JobKey("SocialCurationJob");
            q.AddJob<SocialCurationJob>(opts => opts.WithIdentity(socialJobKey));

            q.AddTrigger(opts => opts
                .ForJob(socialJobKey)
                .WithIdentity("SocialCurationJob-trigger")
                .WithCronSchedule("0 0 8 * * ?", x => x.InTimeZone(warsawTimeZone)));
        });

        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }

    private static TimeZoneInfo GetWarsawTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }
}
