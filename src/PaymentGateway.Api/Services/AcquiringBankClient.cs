using System.Net;
using System.Text;
using System.Text.Json;
using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Services;

public class AcquiringBankClient(HttpClient httpClient, ILogger<AcquiringBankClient> logger) : IAcquirerClient
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            logger.LogInformation("Sending payment request to acquiring bank for card ending in {CardLastFour}",
                request.CardNumber.Length >= 4 ? request.CardNumber.Substring(request.CardNumber.Length - 4) : "****");

            var response = await httpClient.PostAsync("/payments", content, cancellationToken);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                logger.LogWarning("Acquiring bank service unavailable (503) for card ending in {CardLastFour}",
                    request.CardNumber.Length >= 4 ? request.CardNumber.Substring(request.CardNumber.Length - 4) : "****");
            }

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var acquirerResponse = JsonSerializer.Deserialize<AcquirerPaymentResponse>(responseContent, _jsonOptions);

            logger.LogInformation("Received acquiring bank response for card ending in {CardLastFour}: {Authorized}",
                request.CardNumber.Length >= 4 ? request.CardNumber.Substring(request.CardNumber.Length - 4) : "****",
                acquirerResponse?.Authorized);

            return acquirerResponse ?? new AcquirerPaymentResponse { Authorized = false };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error occurred while processing payment with acquiring bank");
            throw;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Timeout occurred while processing payment with acquiring bank");
            throw;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Error deserializing acquiring bank response");
            throw;
        }
    }
}

