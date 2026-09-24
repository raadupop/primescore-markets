using System.Net;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Classification.Classifier;

/// <summary>
/// 200 means ready; 503 means reachable but its process-local windows are not bootstrapped
/// (see the classifier contract). Anything else, or no answer, is reported as unreachable.
/// </summary>
internal sealed class GetClassifierHealthHandler(
    IHttpClientFactory httpClientFactory,
    IOptions<ClassifierOptions> options,
    IClock clock) : IQueryHandler<GetClassifierHealth, ClassifierHealth>
{
    public async Task<ClassifierHealth> HandleAsync(GetClassifierHealth query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient(ClassifierOptions.HttpClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.HealthTimeoutSeconds));
        try
        {
            using var response = await client.GetAsync(new Uri("health", UriKind.Relative), timeout.Token).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => Health(reachable: true, ready: true, detail: null),
                HttpStatusCode.ServiceUnavailable => Health(
                    reachable: true, ready: false,
                    detail: "Classifier reports not_ready: its process-local windows are not bootstrapped."),
                _ => Health(reachable: false, ready: null, detail: $"Unexpected HTTP {(int)response.StatusCode} from /health."),
            };
        }
        catch (HttpRequestException exception)
        {
            return Health(reachable: false, ready: null, detail: exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Health(reachable: false, ready: null, detail: $"No answer within {settings.HealthTimeoutSeconds} s.");
        }

        ClassifierHealth Health(bool reachable, bool? ready, string? detail) =>
            new(reachable, ready, detail, settings.BaseUrl, clock.UtcNow);
    }
}
