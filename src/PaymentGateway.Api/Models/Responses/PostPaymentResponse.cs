using PaymentGateway.Api.Enums;
using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Responses;

public record PostPaymentResponse
{
    public Guid Id { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentStatus Status { get; set; }
    public string CardNumberLastFour { get; set; }
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string Currency { get; set; }
    public int Amount { get; set; }
}
