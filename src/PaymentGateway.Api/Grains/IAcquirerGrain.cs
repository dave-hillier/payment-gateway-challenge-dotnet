using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public interface IAcquirerGrain : IGrainWithStringKey
{
    Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default);

    Task<AcquirerHealthStatus> GetHealthStatusAsync();

    Task<AcquirerMetrics> GetMetricsAsync();

    Task ConfigureAsync(AcquirerConfiguration config);
}

[GenerateSerializer]
public class AcquirerHealthStatus
{
    [Id(0)]
    public bool IsHealthy { get; set; }
    [Id(1)]
    public DateTime LastSuccessfulRequest { get; set; }
    [Id(2)]
    public int FailureCount { get; set; }
    [Id(3)]
    public string? LastError { get; set; }
}

[GenerateSerializer]
public class AcquirerMetrics
{
    [Id(0)]
    public int RequestCount { get; set; }
    [Id(1)]
    public int SuccessCount { get; set; }
    [Id(2)]
    public int FailureCount { get; set; }
    [Id(3)]
    public double AverageResponseTime { get; set; }
    [Id(4)]
    public DateTime WindowStart { get; set; }
}

[GenerateSerializer]
public class AcquirerConfiguration
{
    [Id(0)]
    public string BaseUrl { get; set; } = string.Empty;
    [Id(1)]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    [Id(2)]
    public int MaxRetries { get; set; } = 3;
    [Id(3)]
    public TimeSpan CircuitBreakerThreshold { get; set; } = TimeSpan.FromMinutes(5);
    [Id(4)]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
}