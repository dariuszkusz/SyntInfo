using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyntInfo.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTagsToNewsArticle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<List<string>>(
            name: "Tags",
            table: "NewsArticles",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'::text[]");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Tags",
            table: "NewsArticles");
    }
}
