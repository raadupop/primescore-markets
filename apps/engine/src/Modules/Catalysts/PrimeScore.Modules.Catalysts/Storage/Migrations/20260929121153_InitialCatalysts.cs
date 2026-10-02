using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Catalysts.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class InitialCatalysts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cat_events",
                columns: table => new
                {
                    catalyst_id = table.Column<string>(type: "TEXT", nullable: false),
                    vintage = table.Column<int>(type: "INTEGER", nullable: false),
                    family = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    reference_period = table.Column<string>(type: "TEXT", nullable: true),
                    source_key = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    scheduled_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    scheduled_date = table.Column<string>(type: "TEXT", nullable: false),
                    time_announced = table.Column<bool>(type: "INTEGER", nullable: false),
                    sep = table.Column<bool>(type: "INTEGER", nullable: true),
                    tentative = table.Column<bool>(type: "INTEGER", nullable: false),
                    change = table.Column<string>(type: "TEXT", nullable: true),
                    counted_reschedule = table.Column<bool>(type: "INTEGER", nullable: false),
                    first_announced_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    actual_at_ms = table.Column<long>(type: "INTEGER", nullable: true),
                    adapter = table.Column<string>(type: "TEXT", nullable: false),
                    source_kind = table.Column<string>(type: "TEXT", nullable: false),
                    source_url = table.Column<string>(type: "TEXT", nullable: false),
                    retrieved_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    file_sha256 = table.Column<string>(type: "TEXT", nullable: true),
                    derivation = table.Column<string>(type: "TEXT", nullable: true),
                    backfilled = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_current = table.Column<bool>(type: "INTEGER", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ledger_hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_events", x => new { x.catalyst_id, x.vintage });
                });

            migrationBuilder.CreateIndex(
                name: "ix_cat_events_current_time",
                table: "cat_events",
                columns: new[] { "is_current", "scheduled_at_ms" });

            migrationBuilder.CreateIndex(
                name: "ix_cat_events_family_time",
                table: "cat_events",
                columns: new[] { "family", "scheduled_at_ms" });

            migrationBuilder.CreateIndex(
                name: "ix_cat_events_source_key",
                table: "cat_events",
                columns: new[] { "family", "source_key" });

            migrationBuilder.CreateIndex(
                name: "ix_cat_events_source_url",
                table: "cat_events",
                column: "source_url");

            migrationBuilder.CreateIndex(
                name: "ux_cat_events_sequence",
                table: "cat_events",
                column: "ledger_sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cat_events");
        }
    }
}
