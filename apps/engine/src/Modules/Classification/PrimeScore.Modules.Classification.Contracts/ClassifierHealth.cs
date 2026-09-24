using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Classification.Contracts;

/// <summary>Probe the Python classifier's <c>GET /health</c>.</summary>
public sealed record GetClassifierHealth : IQuery<ClassifierHealth>;

/// <param name="Reachable">The classifier answered over HTTP.</param>
/// <param name="Ready">The classifier's own readiness flag (its process-local windows are bootstrapped); null when unreachable.</param>
/// <param name="Detail">Why the classifier is unreachable or not ready.</param>
public sealed record ClassifierHealth(
    bool Reachable,
    bool? Ready,
    string? Detail,
    string BaseUrl,
    DateTimeOffset CheckedAt);
