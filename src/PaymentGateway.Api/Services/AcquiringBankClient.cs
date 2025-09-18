using System.Net;
using System.Text;
using System.Text.Json;
using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Services;

public class AcquiringBankClient : IAcquirerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AcquiringBankClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public AcquiringBankClient(HttpClient httpClient, ILogger<AcquiringBankClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    public async Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending payment request to acquiring bank for card ending in {CardLastFour}",
                request.CardNumber.Length >= 4 ? request.CardNumber.Substring(request.CardNumber.Length - 4) : "****");

            var response = await _httpClient.PostAsync("/payments", content, cancellationToken);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogWarning("Acquiring bank service unavailable (503) for card ending in {CardLastFour}",
                    request.CardNumber.Length >= 4 ? request.CardNumber.Substring(request.CardNumber.Length - 4) : "****");
            }

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var acquirerResponse = JsonSerializer.Deserialize<AcquirerPaymentResponse>(responseContent, _jsonOptions);

            _logger.LogInformation("Received acquiring bank response for card ending in {CardLastFour}: {Authorized}",
                request.CardNumber.Length >= 4 ? request.CardNumber.Substring(request.CardNumber.Length - 4) : "****",
                acquirerResponse?.Authorized);

            return acquirerResponse ?? new AcquirerPaymentResponse { Authorized = false };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error occurred while processing payment with acquiring bank");
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout occurred while processing payment with acquiring bank");
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error deserializing acquiring bank response");
            throw;
        }
    }
}

