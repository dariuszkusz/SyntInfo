using Microsoft.EntityFrameworkCore;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<NewsArticle> NewsArticles { get; set; } = null!;
    public DbSet<NewsCategory> NewsCategories { get; set; } = null!;
    public DbSet<NewsSource> NewsSources { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Włączenie rozszerzenia pgvector
        modelBuilder.HasPostgresExtension("vector");

        // Konfiguracje dodatkowe dla Postgresa i tabel
        modelBuilder.Entity<NewsArticle>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.SummaryText).IsRequired().HasMaxLength(5000); // Zapas na fallback z LLM
            
            // Konfiguracja wektora (wymiar 1536 dla OpenAI text-embedding-3-small via OpenRouter)
            entity.Property(e => e.Embedding).HasColumnType("vector(1536)"); 
        });

        modelBuilder.Entity<NewsCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<NewsSource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.RssUrl).IsRequired().HasMaxLength(500);
        });
    }
}
