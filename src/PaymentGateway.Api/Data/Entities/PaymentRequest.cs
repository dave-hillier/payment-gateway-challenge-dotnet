using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Data.Entities;

public class PaymentRequest
{
    public Guid Id { get; set; }

    public string CardNumber { get; set; } = string.Empty;
    public string CardNumberLastFour { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string CVV { get; set; } = string.Empty;

    public PaymentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? BankResponseCode { get; set; }

    public int RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }

    public string? IdempotencyKey { get; set; }
}