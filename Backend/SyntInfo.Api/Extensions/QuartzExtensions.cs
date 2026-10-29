using Quartz;
using SyntInfo.Infrastructure.BackgroundJobs;

namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection dla konfiguracji harmonogramu zadań w tle Quartz .NET.
/// </summary>
public static class QuartzExtensions
{
    /// <summary>
    /// Konfiguruje harmonogram zadań w tle Quartz wraz z jobami RssFetcherJob i SocialCurationFallbackJob.
    /// </summary>
    public static IServiceCollection AddQuartzBackgroundJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            var jobKey = new JobKey("RssFetcherJob");
            q.AddJob<RssFetcherJob>(opts => opts.WithIdentity(jobKey));

            var warsawTimeZone = GetWarsawTimeZone();

            // Odpalanie pobierania i opracowywania RSS raz dziennie o 6:00 rano (czasu polskiego)
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity("RssFetcherJob-trigger")
                .WithCronSchedule("0 0 6 * * ?", x => x.InTimeZone(warsawTimeZone)));

            // Odpalanie automatycznego fallbacku o 9:00 rano (czasu polskiego), jeśli redaktor nie zareagował
            var fallbackJobKey = new JobKey("SocialCurationFallbackJob");
            q.AddJob<SocialCurationFallbackJob>(opts => opts.WithIdentity(fallbackJobKey));

            q.AddTrigger(opts => opts
                .ForJob(fallbackJobKey)
                .WithIdentity("SocialCurationFallbackJob-trigger")
                .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(warsawTimeZone)));
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
