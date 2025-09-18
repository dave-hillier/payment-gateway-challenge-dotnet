using Microsoft.EntityFrameworkCore;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Data.Entities;
using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Services;

public class PaymentProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentProcessorService> _logger;
    private readonly IPaymentCompletionService _completionService;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMilliseconds(50);

    public PaymentProcessorService(
        IServiceProvider serviceProvider,
        ILogger<PaymentProcessorService> logger,
        IPaymentCompletionService completionService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _completionService = completionService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment Processor Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingPayments(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payments");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Payment Processor Service stopped");
    }

    private async Task ProcessPendingPayments(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();
        var acquirerClient = scope.ServiceProvider.GetRequiredService<IAcquirerClient>();

        var payment = await GetNextPendingPayment(dbContext, cancellationToken);
        if (payment == null)
            return;

        await ProcessPayment(dbContext, acquirerClient, payment, cancellationToken);
    }

    private async Task<PaymentRequest?> GetNextPendingPayment(PaymentGatewayDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var payment = await dbContext.PaymentRequests
            .Where(p => p.Status == PaymentStatus.Validated &&
                       (p.NextRetryAt == null || p.NextRetryAt <= now))
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment != null)
        {
            payment.Status = PaymentStatus.Processing;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return payment;
    }

    private async Task ProcessPayment(
        PaymentGatewayDbContext dbContext,
        IAcquirerClient acquirerClient,
        PaymentRequest payment,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing payment {PaymentId}", payment.Id);

            var bankResponse = await acquirerClient.ProcessPaymentAsync(
                payment.CardNumber,
                payment.ExpiryMonth,
                payment.ExpiryYear,
                payment.Currency,
                payment.Amount,
                payment.CVV,
                cancellationToken);

            using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            payment = await dbContext.PaymentRequests.FindAsync(new object[] { payment.Id }, cancellationToken);
            if (payment == null)
                return;

            payment.Status = bankResponse.Status switch
            {
                PaymentStatus.Authorized => PaymentStatus.Authorized,
                PaymentStatus.Declined => PaymentStatus.Declined,
                _ => PaymentStatus.Rejected
            };
            payment.ProcessedAt = DateTime.UtcNow;
            payment.BankResponseCode = bankResponse.AuthorizationCode;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Payment {PaymentId} processed with status {Status}", payment.Id, payment.Status);

            // Notify waiting controller that payment is complete
            _completionService.NotifyCompletion(payment);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogError(ex, "Bank service unavailable for payment {PaymentId}", payment.Id);
            await HandlePaymentFailure(dbContext, payment.Id, "SERVICE_UNAVAILABLE", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment {PaymentId}", payment.Id);
            await HandlePaymentFailure(dbContext, payment.Id, null, cancellationToken);
        }
    }

    private async Task HandlePaymentFailure(PaymentGatewayDbContext dbContext, Guid paymentId, string? errorCode, CancellationToken cancellationToken)
    {
        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var payment = await dbContext.PaymentRequests.FindAsync(new object[] { paymentId }, cancellationToken);
        if (payment == null)
            return;

        payment.RetryCount++;

        // If service unavailable, fail immediately without retries
        if (errorCode == "SERVICE_UNAVAILABLE")
        {
            payment.Status = PaymentStatus.Failed;
            payment.ProcessedAt = DateTime.UtcNow;
            payment.BankResponseCode = errorCode;
            _logger.LogWarning("Payment {PaymentId} failed due to bank service unavailable", paymentId);

            // Notify waiting controller that payment has failed
            _completionService.NotifyCompletion(payment);
        }
        else if (payment.RetryCount >= 3)
        {
            payment.Status = PaymentStatus.Failed;
            payment.ProcessedAt = DateTime.UtcNow;
            _logger.LogWarning("Payment {PaymentId} failed after {RetryCount} retries", paymentId, payment.RetryCount);

            // Notify waiting controller that payment has failed
            _completionService.NotifyCompletion(payment);
        }
        else
        {
            payment.Status = PaymentStatus.Validated;
            payment.NextRetryAt = DateTime.UtcNow.AddSeconds(Math.Pow(2, payment.RetryCount));
            _logger.LogInformation("Payment {PaymentId} scheduled for retry #{RetryCount} at {NextRetryAt}",
                paymentId, payment.RetryCount, payment.NextRetryAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}