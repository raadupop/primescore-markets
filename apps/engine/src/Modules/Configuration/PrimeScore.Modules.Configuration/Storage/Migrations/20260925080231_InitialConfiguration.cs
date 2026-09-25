using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class InitialConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cfg_versions",
                columns: table => new
                {
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    changed_by = table.Column<string>(type: "TEXT", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    settings = table.Column<string>(type: "TEXT", nullable: false),
                    changes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_versions", x => x.version);
                });

            migrationBuilder.CreateIndex(
                name: "ux_cfg_versions_sequence",
                table: "cfg_versions",
                column: "ledger_sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfg_versions");
        }
    }
}
