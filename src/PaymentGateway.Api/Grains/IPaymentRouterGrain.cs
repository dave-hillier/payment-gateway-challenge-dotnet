using Orleans;

using PaymentGateway.Api.Models.Routing;
using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public interface IPaymentRouterGrain : IGrainWithStringKey
{
    Task<string> GetAcquirerIdAsync(string cardNumber, string currency);
    Task<AcquirerPaymentResponse> ProcessPaymentAsync(string cardNumber, int expiryMonth, int expiryYear, string currency, int amount, string cvv);
    Task RegisterAcquirerAsync(string acquirerId, string routeKey, bool isDefault = false);
    Task<RouteNode?> GetRouteNodeAsync(string routeKey);
    Task<IReadOnlyList<RouteNode>> GetAllRouteNodesAsync();
    Task<IReadOnlyList<RouteNode>> GetRouteNodesForCardAsync(string cardNumber, string currency);
    Task SetDefaultAcquirerAsync(string routeKey, string acquirerId);
    Task<bool> IsAcquirerRegisteredAsync(string acquirerId, string routeKey);
}