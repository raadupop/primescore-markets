using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Classification.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class AddAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cls_composites",
                columns: table => new
                {
                    composite_id = table.Column<string>(type: "TEXT", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    config_version = table.Column<int>(type: "INTEGER", nullable: false),
                    context = table.Column<string>(type: "TEXT", nullable: false),
                    trigger_assessment_id = table.Column<string>(type: "TEXT", nullable: false),
                    trigger_signal_id = table.Column<string>(type: "TEXT", nullable: false),
                    as_of_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    computed_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    score = table.Column<double>(type: "REAL", nullable: false),
                    weighting_scheme_id = table.Column<string>(type: "TEXT", nullable: false),
                    aggregation = table.Column<string>(type: "TEXT", nullable: false),
                    contributing = table.Column<string>(type: "TEXT", nullable: false),
                    absent = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cls_composites", x => x.composite_id);
                });

            migrationBuilder.CreateTable(
                name: "cls_dislocations",
                columns: table => new
                {
                    dislocation_id = table.Column<string>(type: "TEXT", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    config_version = table.Column<int>(type: "INTEGER", nullable: false),
                    context = table.Column<string>(type: "TEXT", nullable: false),
                    composite_id = table.Column<string>(type: "TEXT", nullable: false),
                    as_of_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    computed_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    composite_score = table.Column<double>(type: "REAL", nullable: false),
                    reference_instrument = table.Column<string>(type: "TEXT", nullable: false),
                    market_observed_iv = table.Column<double>(type: "REAL", nullable: false),
                    iv_observed_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    iv_signal_id = table.Column<string>(type: "TEXT", nullable: false),
                    regime = table.Column<string>(type: "TEXT", nullable: false),
                    regime_percentile = table.Column<double>(type: "REAL", nullable: true),
                    regime_history = table.Column<int>(type: "INTEGER", nullable: false),
                    sensitivity_factor = table.Column<double>(type: "REAL", nullable: false),
                    signal_implied_iv = table.Column<double>(type: "REAL", nullable: false),
                    dislocation_value = table.Column<double>(type: "REAL", nullable: false),
                    threshold = table.Column<double>(type: "REAL", nullable: false),
                    threshold_breached = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cls_dislocations", x => x.dislocation_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cls_composites_context_time",
                table: "cls_composites",
                columns: new[] { "context", "as_of_ms", "ledger_sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_cls_composites_trigger",
                table: "cls_composites",
                columns: new[] { "context", "trigger_assessment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cls_dislocations_composite",
                table: "cls_dislocations",
                column: "composite_id");

            migrationBuilder.CreateIndex(
                name: "ix_cls_dislocations_context_time",
                table: "cls_dislocations",
                columns: new[] { "context", "as_of_ms", "ledger_sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cls_composites");

            migrationBuilder.DropTable(
                name: "cls_dislocations");
        }
    }
}
