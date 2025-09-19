using System.Text.Json.Serialization;
using Orleans;

namespace PaymentGateway.Api.Models.Acquirer;

[GenerateSerializer]
public record AcquirerPaymentRequest
{
    [JsonPropertyName("card_number")]
    [Id(0)]
    public string CardNumber { get; set; } = "";

    [JsonPropertyName("expiry_date")]
    [Id(1)]
    public string ExpiryDate { get; set; } = "";

    [JsonPropertyName("currency")]
    [Id(2)]
    public string Currency { get; set; } = "";

    [JsonPropertyName("amount")]
    [Id(3)]
    public int Amount { get; set; }

    [JsonPropertyName("cvv")]
    [Id(4)]
    public string Cvv { get; set; } = "";
}