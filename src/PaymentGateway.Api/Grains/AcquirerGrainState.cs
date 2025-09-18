namespace PaymentGateway.Api.Grains;

[GenerateSerializer]
public class AcquirerGrainState
{
    [Id(0)]
    public AcquirerConfiguration Configuration { get; set; } = new();

    [Id(1)]
    public bool IsHealthy { get; set; } = true;

    [Id(2)]
    public DateTime LastSuccessfulRequest { get; set; } = DateTime.UtcNow;

    [Id(3)]
    public int FailureCount { get; set; } = 0;

    [Id(4)]
    public string? LastError { get; set; }

    [Id(5)]
    public DateTime CircuitBreakerOpenedAt { get; set; }

    [Id(6)]
    public bool IsCircuitBreakerOpen { get; set; } = false;

    [Id(7)]
    public List<RequestMetric> RecentRequests { get; set; } = new();

    [Id(8)]
    public DateTime MetricsWindowStart { get; set; } = DateTime.UtcNow;

    [Id(9)]
    public Dictionary<string, int> PaymentRetryCount { get; set; } = new();
}

[GenerateSerializer]
public class RequestMetric
{
    [Id(0)]
    public DateTime Timestamp { get; set; }

    [Id(1)]
    public bool IsSuccess { get; set; }

    [Id(2)]
    public TimeSpan ResponseTime { get; set; }

    [Id(3)]
    public string? ErrorMessage { get; set; }
}