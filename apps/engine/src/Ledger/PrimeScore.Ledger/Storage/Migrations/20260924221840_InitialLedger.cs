using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Ledger.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    entity_id = table.Column<string>(type: "TEXT", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    config_version = table.Column<int>(type: "INTEGER", nullable: false),
                    summary = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    prev_hash = table.Column<string>(type: "TEXT", nullable: false),
                    hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger", x => x.sequence);
                });

            migrationBuilder.CreateTable(
                name: "ledger_verifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    verified_at = table.Column<string>(type: "TEXT", nullable: false),
                    ok = table.Column<bool>(type: "INTEGER", nullable: false),
                    entries_checked = table.Column<long>(type: "INTEGER", nullable: false),
                    head_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    head_hash = table.Column<string>(type: "TEXT", nullable: false),
                    first_invalid_sequence = table.Column<long>(type: "INTEGER", nullable: true),
                    reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_verifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "obs_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    stage = table.Column<string>(type: "TEXT", nullable: false),
                    timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    entity_id = table.Column<string>(type: "TEXT", nullable: false),
                    config_version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_obs_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_correlation_id",
                table: "ledger",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entity_id",
                table: "ledger",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_kind",
                table: "ledger",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_obs_log_correlation_id",
                table: "obs_log",
                column: "correlation_id");

            // Append-only at the storage boundary (SRS AUD-001). If the triggers are dropped, the hash
            // chain detects edits at or below the last verified head (SRS AUD-002; ADR-0003 limits).
            migrationBuilder.Sql(
                "CREATE TRIGGER ledger_reject_update BEFORE UPDATE ON ledger " +
                "BEGIN SELECT RAISE(ABORT, 'ledger is append-only'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER ledger_reject_delete BEFORE DELETE ON ledger " +
                "BEGIN SELECT RAISE(ABORT, 'ledger is append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ledger_reject_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ledger_reject_delete;");

            migrationBuilder.DropTable(
                name: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_verifications");

            migrationBuilder.DropTable(
                name: "obs_log");
        }
    }
}
