using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Decision.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class InitialDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dec_decisions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "TEXT", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    config_version = table.Column<int>(type: "INTEGER", nullable: false),
                    context = table.Column<string>(type: "TEXT", nullable: false),
                    outcome = table.Column<string>(type: "TEXT", nullable: false),
                    scenario = table.Column<string>(type: "TEXT", nullable: false),
                    composite_id = table.Column<string>(type: "TEXT", nullable: false),
                    composite_score = table.Column<double>(type: "REAL", nullable: false),
                    dislocation_id = table.Column<string>(type: "TEXT", nullable: false),
                    dislocation_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    dislocation_value = table.Column<double>(type: "REAL", nullable: false),
                    dislocation_threshold = table.Column<double>(type: "REAL", nullable: false),
                    reference_instrument = table.Column<string>(type: "TEXT", nullable: false),
                    market_observed_iv = table.Column<double>(type: "REAL", nullable: false),
                    signal_implied_iv = table.Column<double>(type: "REAL", nullable: false),
                    trigger_signal_id = table.Column<string>(type: "TEXT", nullable: false),
                    as_of_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dec_decisions", x => x.decision_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dec_decisions_context_time",
                table: "dec_decisions",
                columns: new[] { "context", "as_of_ms", "ledger_sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_dec_decisions_correlation",
                table: "dec_decisions",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_dec_decisions_time",
                table: "dec_decisions",
                column: "as_of_ms");

            migrationBuilder.CreateIndex(
                name: "ux_dec_decisions_dislocation",
                table: "dec_decisions",
                column: "dislocation_sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_dec_decisions_sequence",
                table: "dec_decisions",
                column: "ledger_sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dec_decisions");
        }
    }
}
