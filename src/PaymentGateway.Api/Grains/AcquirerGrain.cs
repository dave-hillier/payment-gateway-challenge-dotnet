using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Orleans;
using Orleans.Runtime;
using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public class AcquirerGrain : Grain, IAcquirerGrain
{
    private readonly IPersistentState<AcquirerGrainState> _state;
    private readonly ILogger<AcquirerGrain> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JsonSerializerOptions _jsonOptions;

    public AcquirerGrain(
        [PersistentState("acquirerState", "acquirerStore")] IPersistentState<AcquirerGrainState> state,
        ILogger<AcquirerGrain> logger,
        IHttpClientFactory httpClientFactory)
    {
        _state = state;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var bankId = this.GetPrimaryKeyString();

        // Ensure configuration is set before processing
        if (string.IsNullOrEmpty(_state.State.Configuration.BaseUrl))
        {
            _logger.LogError("Acquirer {BankId} is not configured - BaseUrl is empty", bankId);
            throw new InvalidOperationException($"Acquirer {bankId} is not properly configured");
        }

        // Check circuit breaker
        if (IsCircuitBreakerOpen())
        {
            _logger.LogWarning("Circuit breaker is open for acquirer {BankId}, rejecting request", bankId);
            throw new HttpRequestException("Acquirer service is temporarily unavailable", null, HttpStatusCode.ServiceUnavailable);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("AcquirerClient");

            // Override base configuration with acquirer-specific settings if needed
            if (!string.IsNullOrEmpty(_state.State.Configuration.BaseUrl) &&
                httpClient.BaseAddress?.ToString() != _state.State.Configuration.BaseUrl)
            {
                httpClient.BaseAddress = new Uri(_state.State.Configuration.BaseUrl);
            }
            if (_state.State.Configuration.Timeout != default)
            {
                httpClient.Timeout = _state.State.Configuration.Timeout;
            }

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending payment request to acquirer {BankId} for card ending in {CardLastFour}",
                bankId, GetCardLastFour(request.CardNumber));

            var response = await httpClient.PostAsync("/payments", content, cancellationToken);
            stopwatch.Stop();

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                await RecordFailure(stopwatch.Elapsed, "Service unavailable");
                _logger.LogWarning("Acquirer {BankId} service unavailable (503) for card ending in {CardLastFour}",
                    bankId, GetCardLastFour(request.CardNumber));
                throw new HttpRequestException("Acquirer service unavailable", null, HttpStatusCode.ServiceUnavailable);
            }

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var acquirerResponse = JsonSerializer.Deserialize<AcquirerPaymentResponse>(responseContent, _jsonOptions);

            await RecordSuccess(stopwatch.Elapsed);

            _logger.LogInformation("Received response from acquirer {BankId} for card ending in {CardLastFour}: {Authorized}",
                bankId, GetCardLastFour(request.CardNumber), acquirerResponse?.Authorized);

            return acquirerResponse ?? new AcquirerPaymentResponse { Authorized = false };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await RecordFailure(stopwatch.Elapsed, ex.Message);
            _logger.LogError(ex, "Error processing payment with acquirer {BankId}", bankId);
            throw;
        }
    }

    public Task<AcquirerHealthStatus> GetHealthStatusAsync()
    {
        return Task.FromResult(new AcquirerHealthStatus
        {
            IsHealthy = _state.State.IsHealthy && !IsCircuitBreakerOpen(),
            LastSuccessfulRequest = _state.State.LastSuccessfulRequest,
            FailureCount = _state.State.FailureCount,
            LastError = _state.State.LastError
        });
    }

    public Task<AcquirerMetrics> GetMetricsAsync()
    {
        CleanupOldMetrics();

        var successCount = _state.State.RecentRequests.Count(r => r.IsSuccess);
        var failureCount = _state.State.RecentRequests.Count(r => !r.IsSuccess);
        var averageResponseTime = _state.State.RecentRequests.Any()
            ? _state.State.RecentRequests.Average(r => r.ResponseTime.TotalMilliseconds)
            : 0;

        return Task.FromResult(new AcquirerMetrics
        {
            RequestCount = _state.State.RecentRequests.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            AverageResponseTime = averageResponseTime,
            WindowStart = _state.State.MetricsWindowStart
        });
    }

    public async Task ConfigureAsync(AcquirerConfiguration config)
    {
        _state.State.Configuration = config;
        await _state.WriteStateAsync();

        _logger.LogInformation("Updated configuration for acquirer {BankId}", this.GetPrimaryKeyString());
    }

    private bool IsCircuitBreakerOpen()
    {
        if (!_state.State.IsCircuitBreakerOpen)
            return false;

        // Check if circuit breaker should be reset
        var elapsed = DateTime.UtcNow - _state.State.CircuitBreakerOpenedAt;
        if (elapsed >= _state.State.Configuration.CircuitBreakerThreshold)
        {
            _state.State.IsCircuitBreakerOpen = false;
            _state.State.FailureCount = 0;
            _logger.LogInformation("Circuit breaker reset for acquirer {BankId}", this.GetPrimaryKeyString());
            return false;
        }

        return true;
    }

    private async Task RecordSuccess(TimeSpan responseTime)
    {
        _state.State.IsHealthy = true;
        _state.State.LastSuccessfulRequest = DateTime.UtcNow;
        _state.State.FailureCount = 0;
        _state.State.LastError = null;
        _state.State.IsCircuitBreakerOpen = false;

        _state.State.RecentRequests.Add(new RequestMetric
        {
            Timestamp = DateTime.UtcNow,
            IsSuccess = true,
            ResponseTime = responseTime
        });

        CleanupOldMetrics();
        await _state.WriteStateAsync();
    }

    private async Task RecordFailure(TimeSpan responseTime, string errorMessage)
    {
        _state.State.IsHealthy = false;
        _state.State.FailureCount++;
        _state.State.LastError = errorMessage;

        _state.State.RecentRequests.Add(new RequestMetric
        {
            Timestamp = DateTime.UtcNow,
            IsSuccess = false,
            ResponseTime = responseTime,
            ErrorMessage = errorMessage
        });

        // Check if circuit breaker should be opened
        if (_state.State.FailureCount >= _state.State.Configuration.CircuitBreakerFailureThreshold)
        {
            _state.State.IsCircuitBreakerOpen = true;
            _state.State.CircuitBreakerOpenedAt = DateTime.UtcNow;

            _logger.LogWarning("Circuit breaker opened for acquirer {BankId} after {FailureCount} failures",
                this.GetPrimaryKeyString(), _state.State.FailureCount);
        }

        CleanupOldMetrics();
        await _state.WriteStateAsync();
    }

    private void CleanupOldMetrics()
    {
        // Keep only last hour of metrics
        var cutoff = DateTime.UtcNow.AddHours(-1);
        _state.State.RecentRequests.RemoveAll(r => r.Timestamp < cutoff);
    }

    private static string GetCardLastFour(string cardNumber)
    {
        return cardNumber.Length >= 4 ? cardNumber.Substring(cardNumber.Length - 4) : "****";
    }
}