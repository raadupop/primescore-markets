using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Ingestion.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class InitialIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ing_rejections",
                columns: table => new
                {
                    rejection_id = table.Column<string>(type: "TEXT", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    source_identifier = table.Column<string>(type: "TEXT", nullable: true),
                    errors = table.Column<string>(type: "TEXT", nullable: false),
                    raw = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ing_rejections", x => x.rejection_id);
                });

            migrationBuilder.CreateTable(
                name: "ing_signals",
                columns: table => new
                {
                    signal_id = table.Column<string>(type: "TEXT", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: false),
                    source_identifier = table.Column<string>(type: "TEXT", nullable: false),
                    instrument = table.Column<string>(type: "TEXT", nullable: false),
                    variant = table.Column<string>(type: "TEXT", nullable: false),
                    provider = table.Column<string>(type: "TEXT", nullable: false),
                    observed_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    payload_type = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<double>(type: "REAL", nullable: true),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    provenance = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ing_signals", x => x.signal_id);
                });

            migrationBuilder.CreateTable(
                name: "ing_source_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    started_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    finished_at_ms = table.Column<long>(type: "INTEGER", nullable: true),
                    succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    accepted = table.Column<int>(type: "INTEGER", nullable: false),
                    duplicates = table.Column<int>(type: "INTEGER", nullable: false),
                    revised = table.Column<int>(type: "INTEGER", nullable: false),
                    missing = table.Column<int>(type: "INTEGER", nullable: false),
                    rejected = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ing_source_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ing_rejections_sequence",
                table: "ing_rejections",
                column: "ledger_sequence");

            migrationBuilder.CreateIndex(
                name: "ix_ing_signals_category_time",
                table: "ing_signals",
                columns: new[] { "category", "observed_at_ms" });

            migrationBuilder.CreateIndex(
                name: "ix_ing_signals_provider",
                table: "ing_signals",
                columns: new[] { "provider", "source_identifier", "observed_at_ms" });

            migrationBuilder.CreateIndex(
                name: "ix_ing_signals_series_time",
                table: "ing_signals",
                columns: new[] { "instrument", "category", "variant", "observed_at_ms" });

            migrationBuilder.CreateIndex(
                name: "ix_ing_signals_time",
                table: "ing_signals",
                column: "observed_at_ms");

            migrationBuilder.CreateIndex(
                name: "ux_ing_signals_key",
                table: "ing_signals",
                columns: new[] { "source_identifier", "instrument", "variant", "observed_at_ms" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ing_source_runs_source_time",
                table: "ing_source_runs",
                columns: new[] { "source", "started_at_ms" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ing_rejections");

            migrationBuilder.DropTable(
                name: "ing_signals");

            migrationBuilder.DropTable(
                name: "ing_source_runs");
        }
    }
}
