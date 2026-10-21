using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddViewsAdsHouseDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                table: "translation_houses",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cover_key",
                table: "translation_houses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "founded_year",
                table: "translation_houses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "recruiting",
                table: "translation_houses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "recruitment_info",
                table: "translation_houses",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tagline",
                table: "translation_houses",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extra_pages",
                table: "processing_jobs",
                type: "jsonb",
                nullable: true,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "chapter_view_daily",
                columns: table => new
                {
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    comic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    translation_house_id = table.Column<Guid>(type: "uuid", nullable: false),
                    views = table.Column<long>(type: "bigint", nullable: false),
                    suspicious_views = table.Column<long>(type: "bigint", nullable: false),
                    revenue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chapter_view_daily", x => new { x.chapter_id, x.day });
                });

            migrationBuilder.CreateTable(
                name: "view_receipts",
                columns: table => new
                {
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    visitor_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_view_receipts", x => new { x.chapter_id, x.day, x.visitor_hash });
                });

            migrationBuilder.CreateTable(
                name: "view_rejection_daily",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_view_rejection_daily", x => new { x.day, x.reason });
                });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_view_daily_comic_id_day",
                table: "chapter_view_daily",
                columns: new[] { "comic_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_view_daily_day",
                table: "chapter_view_daily",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_view_daily_translation_house_id_day",
                table: "chapter_view_daily",
                columns: new[] { "translation_house_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_view_receipts_day",
                table: "view_receipts",
                column: "day");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_view_daily");

            migrationBuilder.DropTable(
                name: "view_receipts");

            migrationBuilder.DropTable(
                name: "view_rejection_daily");

            migrationBuilder.DropColumn(
                name: "contact_email",
                table: "translation_houses");

            migrationBuilder.DropColumn(
                name: "cover_key",
                table: "translation_houses");

            migrationBuilder.DropColumn(
                name: "founded_year",
                table: "translation_houses");

            migrationBuilder.DropColumn(
                name: "recruiting",
                table: "translation_houses");

            migrationBuilder.DropColumn(
                name: "recruitment_info",
                table: "translation_houses");

            migrationBuilder.DropColumn(
                name: "tagline",
                table: "translation_houses");

            migrationBuilder.DropColumn(
                name: "extra_pages",
                table: "processing_jobs");
        }
    }
}
