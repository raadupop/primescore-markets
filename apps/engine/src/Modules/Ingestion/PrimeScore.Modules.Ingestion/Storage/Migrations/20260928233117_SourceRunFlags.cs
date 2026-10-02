using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Ingestion.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class SourceRunFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "flags",
                table: "ing_source_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "ing_source_runs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "flags",
                table: "ing_source_runs");

            migrationBuilder.DropColumn(
                name: "note",
                table: "ing_source_runs");
        }
    }
}
