using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrimeScore.Ledger.Storage.Migrations
{
    /// <inheritdoc />
    public partial class HardenAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BEFORE INSERT fires before conflict resolution, so INSERT OR REPLACE and REPLACE INTO
            // cannot overwrite an entry; only the next contiguous sequence is accepted (SRS AUD-001).
            migrationBuilder.Sql(
                "CREATE TRIGGER ledger_reject_out_of_order BEFORE INSERT ON ledger " +
                "WHEN NEW.sequence <> (SELECT IFNULL(MAX(sequence), 0) + 1 FROM ledger) " +
                "BEGIN SELECT RAISE(ABORT, 'ledger is append-only'); END;");

            // Verification results anchor truncation detection; they are append-only too.
            migrationBuilder.Sql(
                "CREATE TRIGGER ledger_verifications_reject_update BEFORE UPDATE ON ledger_verifications " +
                "BEGIN SELECT RAISE(ABORT, 'ledger verifications are append-only'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER ledger_verifications_reject_delete BEFORE DELETE ON ledger_verifications " +
                "BEGIN SELECT RAISE(ABORT, 'ledger verifications are append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ledger_reject_out_of_order;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ledger_verifications_reject_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ledger_verifications_reject_delete;");
        }
    }
}
