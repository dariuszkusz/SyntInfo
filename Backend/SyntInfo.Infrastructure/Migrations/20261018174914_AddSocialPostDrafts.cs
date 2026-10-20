using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyntInfo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialPostDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialPostDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExternalPostId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPostDrafts_NewsArticles_NewsArticleId",
                        column: x => x.NewsArticleId,
                        principalTable: "NewsArticles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostDrafts_CreatedAt",
                table: "SocialPostDrafts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostDrafts_NewsArticleId_PlatformType",
                table: "SocialPostDrafts",
                columns: new[] { "NewsArticleId", "PlatformType" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostDrafts_Status",
                table: "SocialPostDrafts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPostDrafts");
        }
    }
}
