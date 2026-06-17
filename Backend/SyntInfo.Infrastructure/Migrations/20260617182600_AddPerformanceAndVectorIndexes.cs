using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyntInfo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceAndVectorIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_Embedding",
                table: "NewsArticles",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_IsActive_PublishedAt",
                table: "NewsArticles",
                columns: new[] { "IsActive", "PublishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_Embedding",
                table: "NewsArticles");

            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_IsActive_PublishedAt",
                table: "NewsArticles");
        }
    }
}
