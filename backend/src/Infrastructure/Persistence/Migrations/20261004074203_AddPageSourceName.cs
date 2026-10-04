using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPageSourceName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_name",
                table: "chapter_pages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_name",
                table: "chapter_pages");
        }
    }
}
