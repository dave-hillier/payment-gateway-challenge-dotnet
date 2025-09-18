using Microsoft.EntityFrameworkCore;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Data.Entities;

namespace PaymentGateway.Api.Services;

public class IdempotencyService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public IdempotencyService(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task StoreAsync(string key, object response, int statusCode)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();

        var existingPayment = await dbContext.PaymentRequests
            .FirstOrDefaultAsync(p => p.IdempotencyKey == key);

        if (existingPayment != null)
        {
            // Update existing record (though this shouldn't happen in normal flow)
            return;
        }
    }

    public async Task<IdempotencyRecord?> GetAsync(string key)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();

        var payment = await dbContext.PaymentRequests
            .FirstOrDefaultAsync(p => p.IdempotencyKey == key);

        if (payment == null)
        {
            return null;
        }

        // Check if the payment is too old (24 hours)
        if (DateTime.UtcNow - payment.CreatedAt > TimeSpan.FromHours(24))
        {
            return null;
        }

        // Create the payment response object from the database record
        var paymentResponse = new
        {
            id = payment.Id,
            status = payment.Status,
            cardNumberLastFour = payment.CardNumberLastFour,
            expiryMonth = payment.ExpiryMonth,
            expiryYear = payment.ExpiryYear,
            currency = payment.Currency,
            amount = payment.Amount
        };

        // Return a record with the actual payment response
        return new IdempotencyRecord
        {
            PaymentId = payment.Id,
            Response = paymentResponse,
            StatusCode = GetStatusCode(payment),
            CreatedAt = payment.CreatedAt
        };
    }

    public async Task<bool> MarkAsProcessingAsync(string key)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();

        // Check if payment already exists with this idempotency key
        var existingPayment = await dbContext.PaymentRequests
            .FirstOrDefaultAsync(p => p.IdempotencyKey == key);

        return existingPayment == null; // Return true if we can proceed, false if duplicate
    }

    private static int GetStatusCode(PaymentRequest payment)
    {
        return payment.Status switch
        {
            Enums.PaymentStatus.Authorized => 200,
            Enums.PaymentStatus.Declined => 200,
            Enums.PaymentStatus.Rejected => 400,
            _ => 202 // Processing
        };
    }
}

public class IdempotencyRecord
{
    public Guid? PaymentId { get; set; }
    public object? Response { get; set; }
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
}