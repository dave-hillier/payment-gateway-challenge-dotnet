using System.Net;
using System.Text;
using System.Text.Json;
using Orleans;
using Orleans.Runtime;
using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public class AcquirerGrain : Grain, IAcquirerGrain
{
    private readonly IPersistentState<AcquirerGrainState> _state;
    private readonly ILogger<AcquirerGrain> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JsonSerializerOptions _jsonOptions;

    public AcquirerGrain(
        [PersistentState("acquirerState", "acquirerStore")] IPersistentState<AcquirerGrainState> state,
        ILogger<AcquirerGrain> logger,
        IHttpClientFactory httpClientFactory)
    {
        _state = state;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    public async Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var acquirerId = this.GetPrimaryKeyString();

        // Ensure configuration is set before processing
        if (string.IsNullOrEmpty(_state.State.BaseUrl))
        {
            _logger.LogError("Acquirer {AcquirerId} is not configured - BaseUrl is empty", acquirerId);
            throw new InvalidOperationException($"Acquirer {acquirerId} is not properly configured");
        }

        using var httpClient = _httpClientFactory.CreateClient("AcquirerClient");

        // Configure HTTP client for this acquirer
        httpClient.BaseAddress = new Uri(_state.State.BaseUrl);
        httpClient.Timeout = _state.State.Timeout;

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("/payments", content, cancellationToken);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogWarning("Acquirer {AcquirerId} service unavailable (503) for card ending in {CardLastFour}",
                acquirerId, GetCardLastFour(request.CardNumber));
            throw new HttpRequestException("Acquirer service unavailable", null, HttpStatusCode.ServiceUnavailable);
        }

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var acquirerResponse = JsonSerializer.Deserialize<AcquirerPaymentResponse>(responseContent, _jsonOptions);

        return acquirerResponse ?? new AcquirerPaymentResponse { Authorized = false };
    }

    public async Task ConfigureAsync(string baseUrl, TimeSpan timeout)
    {
        _state.State.BaseUrl = baseUrl;
        _state.State.Timeout = timeout;
        await _state.WriteStateAsync();

        _logger.LogInformation("Updated configuration for acquirer {AcquirerId}", this.GetPrimaryKeyString());
    }

    private static string GetCardLastFour(string cardNumber)
    {
        return cardNumber.Length >= 4 ? cardNumber[^4..] : "****";
    }
}