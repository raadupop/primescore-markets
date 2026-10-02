using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Opec;

/// <summary>
/// OPEC and JMMC meetings from the operator-curated CSV (source kind Curated): opec.org serves its
/// listings behind a browser challenge that the engine does not work around (ADR-0011). The file is
/// read whole, like an import: any invalid row, or a row of another family, records nothing and
/// lists every problem with its line. Rows keep their own source URL, retrieval time and checker;
/// the snapshot carries <c>file:&lt;name&gt;</c> and the SHA-256 of the bytes read. No coverage is
/// claimed, so a row removed from the file changes nothing.
/// </summary>
internal sealed class OpecCalendarAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<OpecCalendarOptions> options,
    CatalystRecorder recorder,
    CatalystsReadStore reads,
    IClock clock,
    ILogger<OpecCalendarAdapter> logger) : CalendarAdapter(httpClientFactory, recorder, reads, clock, logger)
{
    private static readonly CatalystFamily[] Families = [CatalystFamily.Opec];

    public override string Name => OpecCalendarOptions.SourceName;

    public override string Description =>
        "OPEC and JMMC meetings from the operator-curated CSV (Sources:OpecCalendar:File); each row names its source, retrieval time and checker.";

    protected override CalendarOptions? ReadSettings(out string? invalid) =>
        CalendarOptions.TryRead(options, OpecCalendarOptions.Section, out invalid);

    protected override string ReadSummary(CalendarRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.PagesRead == 1 ? "file read" : "file not read";
    }

    protected override async Task ReadPagesAsync(CalendarRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var path = options.Value.File;
        var name = Path.GetFileName(path);
        run.PagesAttempted++;
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            run.Errors.Add($"{name}: file not read ({exception.GetType().Name})");
            LogPageFailed(Name, name, exception.GetType().Name);
            return;
        }

        var retrievedAt = Clock.UtcNow;
        var file = CatalystCsv.Parse(Encoding.UTF8.GetString(bytes), retrievedAt);
        var errors = file.Errors.Concat(file.Rows.Where(row => row.Family != CatalystFamily.Opec).Select(row => row.Family).Distinct()
            .Select(family => $"rows of family {family.ToWireName()} belong in import-catalysts, not the OPEC calendar")).ToArray();
        if (errors.Length > 0)
        {
            run.Errors.AddRange(errors.Select(error => $"{name}: {error}"));
            return;
        }

        run.PagesRead++;
        run.RowsExamined += file.Rows.Count;
        if (file.Rows.Count > 0)
        {
            run.Snapshots.Add(new CalendarSnapshot(
                Name, CatalystSourceKind.Curated, "file:" + name, retrievedAt, Convert.ToHexStringLower(SHA256.HashData(bytes)), Families,
                CoverageFrom: null, CoverageTo: null, ProximityDays: 0, file.Rows));
        }
    }
}
