using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyntInfo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalTitle",
                table: "NewsArticles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalTitle",
                table: "NewsArticles");
        }
    }
}
