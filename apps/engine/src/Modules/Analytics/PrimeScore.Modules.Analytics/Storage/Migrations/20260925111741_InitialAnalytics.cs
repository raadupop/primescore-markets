using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Analytics.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class InitialAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ana_replays",
                columns: table => new
                {
                    replay_id = table.Column<string>(type: "TEXT", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ana_replays", x => x.replay_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ana_replays_ledger_sequence",
                table: "ana_replays",
                column: "ledger_sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ana_replays");
        }
    }
}
