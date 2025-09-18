using System.Collections.Concurrent;

namespace PaymentGateway.Api.Services;

public class IdempotencyService
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _cache = new();

    public void Store(string key, object response, int statusCode)
    {
        var record = new IdempotencyRecord
        {
            Response = response,
            StatusCode = statusCode,
            CreatedAt = DateTime.UtcNow
        };

        _cache.AddOrUpdate(key, record, (k, v) => record);
    }

    public IdempotencyRecord? Get(string key)
    {
        if (!_cache.TryGetValue(key, out var record))
        {
            return null;
        }

        if (DateTime.UtcNow - record.CreatedAt < TimeSpan.FromHours(24))
        {
            return record;
        }

        _cache.TryRemove(key, out _);

        return null;
    }

    public void MarkAsProcessing(string key)
    {
        var record = new IdempotencyRecord
        {
            Response = null,
            StatusCode = 0,
            CreatedAt = DateTime.UtcNow
        };

        _cache.TryAdd(key, record);
    }
}

public class IdempotencyRecord
{
    public object? Response { get; set; }
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
}