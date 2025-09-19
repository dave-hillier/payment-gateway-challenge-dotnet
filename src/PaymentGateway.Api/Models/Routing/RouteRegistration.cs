using Orleans;

namespace PaymentGateway.Api.Models.Routing;

[GenerateSerializer]
public class RouteRegistration
{
    [Id(0)]
    public string AcquirerId { get; set; } = string.Empty;

    [Id(1)]
    public string RouteKey { get; set; } = string.Empty;

    [Id(2)]
    public bool IsDefault { get; set; }

    [Id(3)]
    public DateTime RegisteredAt { get; set; }

    [Id(4)]
    public Dictionary<string, string> Configuration { get; set; } = new();

    public RouteRegistration()
    {
    }

    public RouteRegistration(string acquirerId, string routeKey, bool isDefault = false, Dictionary<string, string>? configuration = null)
    {
        AcquirerId = acquirerId ?? throw new ArgumentNullException(nameof(acquirerId));
        RouteKey = routeKey ?? throw new ArgumentNullException(nameof(routeKey));
        IsDefault = isDefault;
        RegisteredAt = DateTime.UtcNow;
        Configuration = configuration ?? new Dictionary<string, string>();
    }
}