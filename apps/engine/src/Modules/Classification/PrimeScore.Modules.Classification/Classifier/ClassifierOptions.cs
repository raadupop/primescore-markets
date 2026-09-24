namespace PrimeScore.Modules.Classification.Classifier;

/// <summary>Bound from <c>Classifier</c>. The Python service is constant infrastructure (AGENTS.md).</summary>
internal sealed class ClassifierOptions
{
    public const string Section = "Classifier";

    public const string HttpClientName = "classifier";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8000";

    public int TimeoutSeconds { get; set; } = 30;

    public int HealthTimeoutSeconds { get; set; } = 3;
}
