using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Acquirer;

public record AcquirerPaymentResponse
{
    [JsonPropertyName("authorized")]
    public bool Authorized { get; set; }

    [JsonPropertyName("authorization_code")]
    public string? AuthorizationCode { get; set; }
}