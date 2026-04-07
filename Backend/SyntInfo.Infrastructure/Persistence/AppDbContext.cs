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
            entity.Property(e => e.SummaryText).IsRequired().HasMaxLength(1500); // Max 500 znakow dla UI, ale dajemy zapas
            
            // Konfiguracja wektora (wymiar 3072 dla Llama 3.2 lub wg potrzeb modelu)
            // Llama 3.2 1B ma zazwyczaj 2048, 3B ma 3072. 
            // Można też zostawić bez wymiaru jeśli model go definiuje dynamicznie.
            entity.Property(e => e.Embedding).HasColumnType("vector(3072)"); 
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
