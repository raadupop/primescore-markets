using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Sources;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Catalysts.Features;

/// <summary>
/// Records an operator-curated catalyst CSV (the back-fill path, ADR-0011) as one snapshot of
/// source kind Curated from adapter <c>import</c>. Rows keep their own source URL and retrieval
/// time; the snapshot carries the file's SHA-256. A file with any invalid row records nothing.
/// </summary>
internal sealed class ImportCatalystsHandler(CatalystRecorder recorder, IClock clock) : ICommandHandler<ImportCatalysts, ImportCatalystsAck>
{
    public const string Adapter = "import";

    public async Task<ImportCatalystsAck> HandleAsync(ImportCatalysts command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.UtcNow;
        var file = CatalystCsv.Parse(command.Csv, now);
        if (file.Errors.Count > 0)
        {
            return new ImportCatalystsAck(0, 0, 0, [], file.Errors);
        }

        var snapshot = new CalendarSnapshot(
            Adapter, CatalystSourceKind.Curated, "file:" + command.FileName, now, command.FileSha256.ToLowerInvariant(),
            file.Rows.Select(row => row.Family).Distinct().ToArray(), CoverageFrom: null, CoverageTo: null, ProximityDays: 0,
            file.Rows, command.ImportedBy);
        var tally = await recorder.RecordAsync([snapshot], command.Force, cancellationToken).ConfigureAwait(false);
        return new ImportCatalystsAck(tally.Scheduled, tally.Rescheduled, tally.Unchanged, tally.Flags, []);
    }
}
