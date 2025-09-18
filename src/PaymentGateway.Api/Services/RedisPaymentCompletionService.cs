using System.Text.Json;
using PaymentGateway.Api.Data.Entities;
using StackExchange.Redis;

namespace PaymentGateway.Api.Services;

public class RedisPaymentCompletionService : IPaymentCompletionService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPaymentCompletionService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisPaymentCompletionService(
        IConnectionMultiplexer redis,
        ILogger<RedisPaymentCompletionService> logger)
    {
        _redis = redis;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<PaymentRequest?> WaitForCompletionAsync(Guid paymentId, TimeSpan timeout)
    {
        var subscriber = _redis.GetSubscriber();
        var channelName = GetChannelName(paymentId);
        var tcs = new TaskCompletionSource<PaymentRequest>();

        _logger.LogDebug("Waiting for payment completion on channel {Channel}", channelName);

        // Subscribe to the payment completion channel
        await subscriber.SubscribeAsync(RedisChannel.Literal(channelName), (channel, message) =>
        {
            try
            {
                _logger.LogDebug("Received completion message on channel {Channel}: {Message}", channel, message);

                if (message.HasValue)
                {
                    var payment = JsonSerializer.Deserialize<PaymentRequest>(message!, _jsonOptions);
                    if (payment != null)
                    {
                        tcs.TrySetResult(payment);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize payment message: {Message}", message);
                        tcs.TrySetException(new InvalidOperationException("Failed to deserialize payment message"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment completion message");
                tcs.TrySetException(ex);
            }
        });

        // Set up timeout cancellation
        using var cts = new CancellationTokenSource(timeout);
        using var registration = cts.Token.Register(() =>
        {
            _logger.LogDebug("Payment completion timed out for channel {Channel}", channelName);
            tcs.TrySetCanceled();
        });

        try
        {
            var result = await tcs.Task;
            _logger.LogDebug("Payment completion received for {PaymentId}", paymentId);
            return result;
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("Payment completion timed out for {PaymentId}", paymentId);
            return null; // Timeout occurred
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for payment completion for {PaymentId}", paymentId);
            return null;
        }
        finally
        {
            // Cleanup: Unsubscribe from the channel
            try
            {
                await subscriber.UnsubscribeAsync(RedisChannel.Literal(channelName));
                _logger.LogDebug("Unsubscribed from channel {Channel}", channelName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unsubscribing from channel {Channel}", channelName);
            }
        }
    }

    public void NotifyCompletion(PaymentRequest payment)
    {
        var subscriber = _redis.GetSubscriber();
        var channelName = GetChannelName(payment.Id);

        try
        {
            var message = JsonSerializer.Serialize(payment, _jsonOptions);

            _logger.LogDebug("Publishing payment completion to channel {Channel} for payment {PaymentId}",
                channelName, payment.Id);

            // Fire and forget - Redis pub/sub is designed to be fast and non-blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    await subscriber.PublishAsync(RedisChannel.Literal(channelName), message);
                    _logger.LogDebug("Successfully published completion for payment {PaymentId}", payment.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish payment completion for {PaymentId}", payment.Id);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serializing payment completion for {PaymentId}", payment.Id);
        }
    }

    private static string GetChannelName(Guid paymentId) => $"payment:completion:{paymentId}";
}