using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Models.Responses;

[GenerateSerializer]
public record GetPaymentResponse
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public PaymentStatus Status { get; set; }
    [Id(2)] public string CardNumberLastFour { get; set; } = string.Empty;
    [Id(3)] public int ExpiryMonth { get; set; }
    [Id(4)] public int ExpiryYear { get; set; }
    [Id(5)] public string Currency { get; set; } = string.Empty;
    [Id(6)] public int Amount { get; set; }
}