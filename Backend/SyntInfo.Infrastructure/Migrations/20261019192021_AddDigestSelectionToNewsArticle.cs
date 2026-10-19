using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyntInfo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDigestSelectionToNewsArticle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DigestPublishedAt",
                table: "NewsArticles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelectedForDigest",
                table: "NewsArticles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_IsSelectedForDigest_DigestPublishedAt",
                table: "NewsArticles",
                columns: new[] { "IsSelectedForDigest", "DigestPublishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_IsSelectedForDigest_DigestPublishedAt",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "DigestPublishedAt",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "IsSelectedForDigest",
                table: "NewsArticles");
        }
    }
}
