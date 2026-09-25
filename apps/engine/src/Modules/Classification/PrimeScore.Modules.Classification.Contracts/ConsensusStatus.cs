using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Classification.Contracts;

/// <summary>Which consensus files are loaded and which rows were rejected for missing provenance.</summary>
public sealed record GetConsensusStatus : IQuery<ConsensusStatus>;

public sealed record ConsensusStatus(string? Directory, IReadOnlyList<ConsensusFileView> Files);

/// <param name="Error">Why the file could not be read (for example, locked by another program); its last good rows stay in use.</param>
public sealed record ConsensusFileView(string Indicator, int Rows, IReadOnlyList<string> RejectedRows, string? Error = null);

/// <summary>How many signals have each latest outcome, per category (the Sources and health screen).</summary>
public sealed record GetClassificationSummary : IQuery<ClassificationSummary>;

/// <param name="LastAssessedAt">When the newest outcome was recorded; null before the first.</param>
/// <param name="LastRun">The most recent classification run in this process; null before the first.</param>
public sealed record ClassificationSummary(IReadOnlyList<OutcomeCount> Outcomes, DateTimeOffset? LastAssessedAt, ClassificationRunView? LastRun);

/// <param name="FullPass">True for the scheduled pass over every signal, false for a newly recorded batch.</param>
/// <param name="NotSent">Signals not sent because the classifier failed three times in a row; retried on the next run.</param>
public sealed record ClassificationRunView(DateTimeOffset FinishedAt, bool FullPass, int Classified, int Fallbacks, int Unavailable, int NotSent, bool BreakerTripped);

/// <param name="Outcome"><c>assessed</c>, <c>fallback</c>, or an <see cref="UnavailableReason"/> name.</param>
public sealed record OutcomeCount(SourceCategory Category, string Outcome, int Signals);
