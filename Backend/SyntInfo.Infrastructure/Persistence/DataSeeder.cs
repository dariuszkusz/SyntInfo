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

            await SeedCategoriesAsync(dbContext);
            await SeedRssSourcesAsync(dbContext, configuration);
        }

        private static async Task SeedCategoriesAsync(AppDbContext dbContext)
        {
            if (!await dbContext.NewsCategories.AnyAsync(c => c.Name == "General"))
            {
                dbContext.NewsCategories.Add(new NewsCategory { Name = "General" });
                await dbContext.SaveChangesAsync();
            }
        }

        private static async Task SeedRssSourcesAsync(AppDbContext dbContext, IConfiguration configuration)
        {
            var polandSourcesConfig = configuration.GetSection("RssSources:Poland").Get<List<RssSourceConfig>>() ?? new();
            var worldSourcesConfig = configuration.GetSection("RssSources:World").Get<List<RssSourceConfig>>() ?? new();

            bool hasChanges = false;
            var allConfigUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var config in polandSourcesConfig)
            {
                allConfigUrls.Add(config.Url);
                var existing = await dbContext.NewsSources.FirstOrDefaultAsync(s => s.RssUrl == config.Url);
                if (existing == null)
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
                else if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    dbContext.NewsSources.Update(existing);
                    hasChanges = true;
                }
            }

            foreach (var config in worldSourcesConfig)
            {
                allConfigUrls.Add(config.Url);
                var existing = await dbContext.NewsSources.FirstOrDefaultAsync(s => s.RssUrl == config.Url);
                if (existing == null)
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
                else if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    dbContext.NewsSources.Update(existing);
                    hasChanges = true;
                }
            }

            // Deaktywuj źródła z bazy danych, które nie występują już w appsettings.json
            var dbSources = await dbContext.NewsSources.ToListAsync();
            foreach (var dbSource in dbSources)
            {
                if (!allConfigUrls.Contains(dbSource.RssUrl) && dbSource.IsActive)
                {
                    dbSource.IsActive = false;
                    dbContext.NewsSources.Update(dbSource);
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
