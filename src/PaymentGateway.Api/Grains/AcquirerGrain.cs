using System.Net;
using System.Text;
using System.Text.Json;

using Orleans;
using Orleans.Runtime;

using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public class AcquirerGrain(
    ILogger<AcquirerGrain> logger,
    IHttpClientFactory httpClientFactory)
    : Grain, IAcquirerGrain
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var acquirerId = this.GetPrimaryKeyString();

        // HttpClient is pre-configured in Program.cs with base address and timeout

        var httpClientKey = $"Acquirer_{acquirerId}";
        using var httpClient = httpClientFactory.CreateClient(httpClientKey);

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("/payments", content, cancellationToken);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            logger.LogWarning("Acquirer {AcquirerId} service unavailable (503)", acquirerId);
            throw new HttpRequestException("Acquirer service unavailable", null, HttpStatusCode.ServiceUnavailable);
        }

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var acquirerResponse = JsonSerializer.Deserialize<AcquirerPaymentResponse>(responseContent, _jsonOptions);

        return acquirerResponse ?? new AcquirerPaymentResponse { Authorized = false };
    }


    public async Task RegisterForRouteAsync(string routeKey, bool isDefault = false)
    {
        if (string.IsNullOrEmpty(routeKey))
            throw new ArgumentNullException(nameof(routeKey));

        var acquirerId = this.GetPrimaryKeyString();
        var paymentRouterGrain = GrainFactory.GetGrain<IPaymentRouterGrain>("global");

        await paymentRouterGrain.RegisterAcquirerAsync(acquirerId, routeKey, isDefault);

        logger.LogInformation("Acquirer {AcquirerId} registered for route {RouteKey} (default: {IsDefault})",
            acquirerId, routeKey, isDefault);
    }
}