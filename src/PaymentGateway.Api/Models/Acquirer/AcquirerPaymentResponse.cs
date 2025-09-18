using System.Text.Json.Serialization;
using PaymentGateway.Api.Enums;
using Orleans;

namespace PaymentGateway.Api.Models.Acquirer;

[GenerateSerializer]
public record AcquirerPaymentResponse
{
    [JsonPropertyName("authorized")]
    [Id(0)]
    public bool Authorized { get; set; }

    [JsonPropertyName("authorization_code")]
    [Id(1)]
    public string? AuthorizationCode { get; set; }

    public PaymentStatus Status => Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
}