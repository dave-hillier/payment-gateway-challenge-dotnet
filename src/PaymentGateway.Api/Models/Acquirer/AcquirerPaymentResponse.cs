using System.Text.Json.Serialization;
using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Models.Acquirer;

public record AcquirerPaymentResponse
{
    [JsonPropertyName("authorized")]
    public bool Authorized { get; set; }

    [JsonPropertyName("authorization_code")]
    public string? AuthorizationCode { get; set; }

    public PaymentStatus Status => Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
}