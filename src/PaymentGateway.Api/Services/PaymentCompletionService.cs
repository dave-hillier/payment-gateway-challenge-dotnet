using System.Collections.Concurrent;
using PaymentGateway.Api.Data.Entities;

namespace PaymentGateway.Api.Services;

public interface IPaymentCompletionService
{
    Task<PaymentRequest?> WaitForCompletionAsync(Guid paymentId, TimeSpan timeout);
    void NotifyCompletion(PaymentRequest payment);
}

public class InMemoryPaymentCompletionService : IPaymentCompletionService
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PaymentRequest>> _pendingPayments = new();

    public async Task<PaymentRequest?> WaitForCompletionAsync(Guid paymentId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<PaymentRequest>();

        if (!_pendingPayments.TryAdd(paymentId, tcs))
        {
            // Payment already being waited on
            tcs = _pendingPayments[paymentId];
        }

        using var cts = new CancellationTokenSource(timeout);
        using var registration = cts.Token.Register(() =>
        {
            tcs.TrySetCanceled();
            _pendingPayments.TryRemove(paymentId, out _);
        });

        try
        {
            return await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            return null; // Timeout occurred
        }
    }

    public void NotifyCompletion(PaymentRequest payment)
    {
        if (_pendingPayments.TryRemove(payment.Id, out var tcs))
        {
            tcs.TrySetResult(payment);
        }
    }
}