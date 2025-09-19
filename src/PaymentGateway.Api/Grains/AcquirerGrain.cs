using System.Net;
using System.Text;
using System.Text.Json;

using Orleans;
using Orleans.Runtime;

using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public class AcquirerGrain(
    [PersistentState("acquirerState", "acquirerStore")] IPersistentState<AcquirerGrainState> state,
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

        // Ensure configuration is set before processing
        if (string.IsNullOrEmpty(state.State.BaseUrl))
        {
            logger.LogError("Acquirer {AcquirerId} is not configured - BaseUrl is empty", acquirerId);
            throw new InvalidOperationException($"Acquirer {acquirerId} is not properly configured");
        }

        using var httpClient = httpClientFactory.CreateClient("AcquirerClient");

        // Configure HTTP client for this acquirer
        httpClient.BaseAddress = new Uri(state.State.BaseUrl);
        httpClient.Timeout = state.State.Timeout;

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

    public async Task ConfigureAsync(string baseUrl, TimeSpan timeout)
    {
        state.State.BaseUrl = baseUrl;
        state.State.Timeout = timeout;
        await state.WriteStateAsync();

        logger.LogInformation("Updated configuration for acquirer {AcquirerId}", this.GetPrimaryKeyString());
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