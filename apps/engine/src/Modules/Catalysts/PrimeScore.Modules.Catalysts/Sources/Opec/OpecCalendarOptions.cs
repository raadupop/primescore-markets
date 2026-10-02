namespace PrimeScore.Modules.Catalysts.Sources.Opec;

/// <summary>
/// Bound from <c>Sources:OpecCalendar</c>; disabled unless <c>Enabled</c> is true and <see cref="File"/>
/// names an existing file. The composition root defaults <see cref="File"/> to
/// <c>apps/engine/data/catalysts/opec.csv</c>. The CSV is read locally, so the page settings
/// (BaseUrl, UserAgent, timeouts, retries) are not used.
/// </summary>
internal sealed class OpecCalendarOptions() : CalendarOptions(Section, "https://www.opec.org/")
{
    public const string Section = "Sources:OpecCalendar";

    public const string SourceName = "OpecCalendar";

    /// <summary>The operator-curated CSV (the <c>import-catalysts</c> format, OPEC rows only).</summary>
    public string File { get; set; } = "";

    public override string? DisabledReason() =>
        !Enabled ? $"disabled by configuration (set {Section}:Enabled=true)"
        : string.IsNullOrWhiteSpace(File) ? $"{Section}:File must name the curated OPEC CSV"
        : !System.IO.File.Exists(File) ? $"{Section}:File names a file that does not exist"
        : null;
}
