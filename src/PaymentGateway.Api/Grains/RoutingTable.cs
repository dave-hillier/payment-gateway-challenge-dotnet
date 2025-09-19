using Orleans;

using PaymentGateway.Api.Models.Routing;

namespace PaymentGateway.Api.Grains;

[GenerateSerializer]
public class RoutingTable
{
    [Id(0)]
    public Dictionary<string, RouteNode> Routes { get; set; } = new();

    public RouteNode GetOrCreateRouteNode(string key)
    {
        if (!Routes.TryGetValue(key, out var node))
        {
            node = new RouteNode(key);
            Routes[key] = node;
        }
        return node;
    }

    public RouteNode? GetRouteNode(string key)
    {
        return Routes.TryGetValue(key, out var node) ? node : null;
    }
    
    public IReadOnlyList<RouteNode> GetMatchingRoutes(string cardNumber, string currency)
    {
        return Routes.Values
            .Where(node => node.Matches(cardNumber, currency))
            .OrderByDescending(node => node.GetSpecificity())
            .ToList();
    }
}