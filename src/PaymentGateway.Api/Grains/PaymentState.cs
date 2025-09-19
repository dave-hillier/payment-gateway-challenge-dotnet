using Orleans;

using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Grains;

[GenerateSerializer]
public class PaymentState
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string CardNumber { get; set; } = string.Empty;
    [Id(2)] public string CardNumberLastFour { get; set; } = string.Empty;
    [Id(3)] public int ExpiryMonth { get; set; }
    [Id(4)] public int ExpiryYear { get; set; }
    [Id(5)] public string Currency { get; set; } = string.Empty;
    [Id(6)] public int Amount { get; set; }
    [Id(7)] public string CVV { get; set; } = string.Empty;
    [Id(8)] public PaymentStatus Status { get; set; } = PaymentStatus.None;
    [Id(9)] public DateTime CreatedAt { get; set; }
    [Id(10)] public DateTime? ProcessedAt { get; set; }
    [Id(11)] public string? BankResponseCode { get; set; }
    [Id(12)] public string? IdempotencyKey { get; set; }
}