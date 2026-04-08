using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Persistence
{
    public class RssSourceConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public static class DataSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // Wykonaj migracje jesli jakichs brakuje (przydatne przy dewelopmencie).
            await dbContext.Database.MigrateAsync();

            await SeedRssSourcesAsync(dbContext, configuration);
        }

        private static async Task SeedRssSourcesAsync(AppDbContext dbContext, IConfiguration configuration)
        {
            var polandSourcesConfig = configuration.GetSection("RssSources:Poland").Get<List<RssSourceConfig>>();
            var worldSourcesConfig = configuration.GetSection("RssSources:World").Get<List<RssSourceConfig>>();

            bool hasChanges = false;

            if (polandSourcesConfig != null)
            {
                foreach (var config in polandSourcesConfig)
                {
                    if (!await dbContext.NewsSources.AnyAsync(s => s.RssUrl == config.Url))
                    {
                        dbContext.NewsSources.Add(new NewsSource
                        {
                            Name = config.Name,
                            RssUrl = config.Url,
                            Region = SourceRegion.Poland,
                            IsActive = true
                        });
                        hasChanges = true;
                    }
                }
            }

            if (worldSourcesConfig != null)
            {
                foreach (var config in worldSourcesConfig)
                {
                    if (!await dbContext.NewsSources.AnyAsync(s => s.RssUrl == config.Url))
                    {
                        dbContext.NewsSources.Add(new NewsSource
                        {
                            Name = config.Name,
                            RssUrl = config.Url,
                            Region = SourceRegion.World,
                            IsActive = true
                        });
                        hasChanges = true;
                    }
                }
            }

            if (hasChanges)
            {
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
