using Orleans;
using PaymentGateway.Api.Grains;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Routes payments to acquirer grains using grain identity as the routing mechanism
/// </summary>
public class AcquirerRouter
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<AcquirerRouter> _logger;

    // BIN Range -> Acquirer Mapping
    private static readonly Dictionary<string, string> BinRangeMapping = new()
    {
        // Visa ranges
        ["4"] = "visa",

        // Mastercard ranges
        ["51"] = "mastercard",
        ["52"] = "mastercard",
        ["53"] = "mastercard",
        ["54"] = "mastercard",
        ["55"] = "mastercard",
        ["2221"] = "mastercard",
        ["2720"] = "mastercard",

        // American Express ranges
        ["34"] = "amex",
        ["37"] = "amex",

        // Discover ranges
        ["6011"] = "discover",
        ["65"] = "discover",

        // For testing/simulator
        ["9999"] = "simulator"
    };

    // Network -> Preferred Acquirer Mapping (for multi-acquirer support)
    private static readonly Dictionary<string, List<string>> NetworkAcquirerMapping = new()
    {
        ["visa"] = new() { "simulator", "firstdata", "chase" },
        ["mastercard"] = new() { "simulator", "worldpay", "stripe" },
        ["amex"] = new() { "simulator", "amex-direct" },
        ["discover"] = new() { "simulator", "discover-direct" }
    };

    public AcquirerRouter(
        IClusterClient clusterClient,
        ILogger<AcquirerRouter> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the acquirer grain for a given card number
    /// </summary>
    public IAcquirerGrain GetAcquirerGrain(string cardNumber, string? preferredAcquirer = null)
    {
        // If preferred acquirer is specified, use it directly
        if (!string.IsNullOrEmpty(preferredAcquirer))
        {
            _logger.LogInformation("Using preferred acquirer {Acquirer} for card {CardLastFour}",
                preferredAcquirer, GetCardLastFour(cardNumber));
            return _clusterClient.GetGrain<IAcquirerGrain>(preferredAcquirer);
        }

        // Determine acquirer based on card BIN
        var acquirerId = DetermineAcquirerId(cardNumber);

        _logger.LogInformation("Routing card {CardLastFour} to acquirer {Acquirer}",
            GetCardLastFour(cardNumber), acquirerId);

        // Return grain with the determined acquirer ID
        return _clusterClient.GetGrain<IAcquirerGrain>(acquirerId);
    }

    /// <summary>
    /// Gets a fallback acquirer grain if primary fails
    /// </summary>
    public IAcquirerGrain GetFallbackAcquirerGrain(string failedAcquirerId)
    {
        // Find network for the failed acquirer
        var network = NetworkAcquirerMapping
            .FirstOrDefault(kvp => kvp.Value.Contains(failedAcquirerId))
            .Key;

        if (string.IsNullOrEmpty(network))
        {
            _logger.LogWarning("No fallback found for acquirer {Acquirer}, using simulator",
                failedAcquirerId);
            return _clusterClient.GetGrain<IAcquirerGrain>("simulator");
        }

        // Get list of acquirers for this network
        var acquirers = NetworkAcquirerMapping[network];

        // Find next acquirer after the failed one
        var failedIndex = acquirers.IndexOf(failedAcquirerId);
        var nextIndex = (failedIndex + 1) % acquirers.Count;
        var fallbackId = acquirers[nextIndex];

        _logger.LogInformation("Using fallback acquirer {Fallback} for failed {Failed}",
            fallbackId, failedAcquirerId);

        return _clusterClient.GetGrain<IAcquirerGrain>(fallbackId);
    }

    private string DetermineAcquirerId(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber) || cardNumber.Length < 6)
        {
            return "simulator";
        }

        // Check BIN ranges from most specific to least specific
        for (int length = Math.Min(6, cardNumber.Length); length > 0; length--)
        {
            var prefix = cardNumber[..length];
            if (BinRangeMapping.TryGetValue(prefix, out var network))
            {
                // For assessment, always use simulator
                // In production, this would select from available acquirers
                return "simulator";

                // Production code would be:
                // return SelectBestAcquirer(network);
            }
        }

        // Default fallback
        return "simulator";
    }

    /// <summary>
    /// Gets the configuration for a specific acquirer
    /// </summary>
    public AcquirerConfiguration GetAcquirerConfiguration(string acquirerId)
    {
        return acquirerId switch
        {
            "simulator" => new AcquirerConfiguration
            {
                BaseUrl = "http://localhost:8080",
                Timeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3,
                CircuitBreakerThreshold = TimeSpan.FromMinutes(5),
                CircuitBreakerFailureThreshold = 5
            },
            "visa" => new AcquirerConfiguration
            {
                BaseUrl = "https://api.visa.com",
                Timeout = TimeSpan.FromSeconds(45),
                MaxRetries = 2,
                CircuitBreakerThreshold = TimeSpan.FromMinutes(3),
                CircuitBreakerFailureThreshold = 3
            },
            "mastercard" => new AcquirerConfiguration
            {
                BaseUrl = "https://api.mastercard.com",
                Timeout = TimeSpan.FromSeconds(40),
                MaxRetries = 2,
                CircuitBreakerThreshold = TimeSpan.FromMinutes(3),
                CircuitBreakerFailureThreshold = 3
            },
            "amex" => new AcquirerConfiguration
            {
                BaseUrl = "https://api.americanexpress.com",
                Timeout = TimeSpan.FromSeconds(35),
                MaxRetries = 1,
                CircuitBreakerThreshold = TimeSpan.FromMinutes(2),
                CircuitBreakerFailureThreshold = 2
            },
            _ => new AcquirerConfiguration
            {
                BaseUrl = "http://localhost:8080", // Default to simulator
                Timeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3
            }
        };
    }

    private static string GetCardLastFour(string cardNumber)
    {
        return cardNumber.Length >= 4
            ? cardNumber[^4..]
            : "****";
    }
}

/// <summary>
/// Extension methods for acquirer grain routing
/// </summary>
public static class AcquirerGrainExtensions
{
    /// <summary>
    /// Creates a composite grain key for advanced routing scenarios
    /// </summary>
    public static string CreateCompositeKey(string network, string region, string merchant)
    {
        return $"{network}:{region}:{merchant}";
    }

    /// <summary>
    /// Parses a composite grain key
    /// </summary>
    public static (string network, string region, string merchant) ParseCompositeKey(string key)
    {
        var parts = key.Split(':');
        return parts.Length == 3
            ? (parts[0], parts[1], parts[2])
            : ("unknown", "unknown", "unknown");
    }
}