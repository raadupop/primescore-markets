using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Modules.Classification.Storage.Migrations
{
    /// <inheritdoc />
    internal partial class InitialClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cls_assessments",
                columns: table => new
                {
                    assessment_id = table.Column<string>(type: "TEXT", nullable: false),
                    ledger_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    signal_id = table.Column<string>(type: "TEXT", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: false),
                    instrument = table.Column<string>(type: "TEXT", nullable: false),
                    variant = table.Column<string>(type: "TEXT", nullable: false),
                    observed_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    assessed_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    available = table.Column<bool>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: true),
                    detail = table.Column<string>(type: "TEXT", nullable: true),
                    http_status = table.Column<int>(type: "INTEGER", nullable: true),
                    score = table.Column<double>(type: "REAL", nullable: true),
                    score_type = table.Column<string>(type: "TEXT", nullable: true),
                    certainty = table.Column<double>(type: "REAL", nullable: true),
                    history_sufficiency = table.Column<double>(type: "REAL", nullable: true),
                    temporal_relevance = table.Column<double>(type: "REAL", nullable: true),
                    classification_method = table.Column<string>(type: "TEXT", nullable: true),
                    event_taxonomy = table.Column<string>(type: "TEXT", nullable: true),
                    is_fallback = table.Column<bool>(type: "INTEGER", nullable: false),
                    staleness_seconds = table.Column<double>(type: "REAL", nullable: true),
                    fallback_of = table.Column<string>(type: "TEXT", nullable: true),
                    flags = table.Column<string>(type: "TEXT", nullable: false),
                    reasoning_trace = table.Column<string>(type: "TEXT", nullable: true),
                    computed_metrics = table.Column<string>(type: "TEXT", nullable: true),
                    reference_window_length = table.Column<int>(type: "INTEGER", nullable: false),
                    consensus = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cls_assessments", x => x.assessment_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cls_assessments_series_time",
                table: "cls_assessments",
                columns: new[] { "instrument", "category", "variant", "observed_at_ms" });

            migrationBuilder.CreateIndex(
                name: "ix_cls_assessments_signal",
                table: "cls_assessments",
                columns: new[] { "signal_id", "ledger_sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_cls_assessments_time",
                table: "cls_assessments",
                column: "observed_at_ms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cls_assessments");
        }
    }
}
