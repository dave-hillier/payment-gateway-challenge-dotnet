using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Data.Entities;
using PaymentGateway.Api.Enums;
using StackExchange.Redis;

namespace PaymentGateway.Api.Services;

public class RedisPaymentCompletionService : IPaymentCompletionService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RedisPaymentCompletionService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisPaymentCompletionService(
        IConnectionMultiplexer redis,
        IServiceProvider serviceProvider,
        ILogger<RedisPaymentCompletionService> logger)
    {
        _redis = redis;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<PaymentRequest?> WaitForCompletionAsync(Guid paymentId, TimeSpan timeout)
    {
        // First, try Redis pub/sub for fast path
        var redisResult = await TryRedisCompletionAsync(paymentId, timeout);
        if (redisResult != null)
        {
            return redisResult;
        }

        // Fallback to database polling if Redis fails or times out
        _logger.LogDebug("Redis completion failed/timed out, falling back to database polling for {PaymentId}", paymentId);
        return await PollDatabaseForCompletionAsync(paymentId, timeout);
    }

    private async Task<PaymentRequest?> TryRedisCompletionAsync(Guid paymentId, TimeSpan timeout)
    {
        try
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
                _logger.LogDebug("Payment completion received via Redis for {PaymentId}", paymentId);
                return result;
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug("Redis completion timed out for {PaymentId}", paymentId);
                return null; // Will trigger database polling fallback
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis completion failed for {PaymentId}, will try database polling", paymentId);
                return null; // Will trigger database polling fallback
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis subscription failed for {PaymentId}, falling back to database polling", paymentId);
            return null; // Will trigger database polling fallback
        }
    }

    private async Task<PaymentRequest?> PollDatabaseForCompletionAsync(Guid paymentId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        const int pollIntervalMs = 100; // Poll every 100ms

        _logger.LogDebug("Starting database polling for payment {PaymentId}", paymentId);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();

                var payment = await dbContext.PaymentRequests
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == paymentId);

                if (payment != null && IsTerminalStatus(payment.Status))
                {
                    _logger.LogDebug("Payment completion found via database polling for {PaymentId}", paymentId);
                    return payment;
                }

                await Task.Delay(pollIntervalMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database polling error for {PaymentId}", paymentId);
                await Task.Delay(pollIntervalMs);
            }
        }

        _logger.LogDebug("Database polling timed out for {PaymentId}", paymentId);
        return null;
    }

    private static bool IsTerminalStatus(PaymentStatus status)
    {
        return status == PaymentStatus.Authorized ||
               status == PaymentStatus.Declined ||
               status == PaymentStatus.Failed;
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