using Orleans;
using Orleans.Runtime;
using PaymentGateway.Api.Models.Routing;
using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public class PaymentRouterGrain : Grain, IPaymentRouterGrain
{
    private readonly IPersistentState<RoutingTable> _state;
    private readonly ILogger<PaymentRouterGrain> _logger;

    public PaymentRouterGrain(
        [PersistentState("routeRegistration", "routeStore")] IPersistentState<RoutingTable> state,
        ILogger<PaymentRouterGrain> logger)
    {
        _state = state;
        _logger = logger;
    }

    public async Task<string> GetAcquirerIdAsync(string cardNumber, string currency)
    {
        var matchingRoutes = await GetRouteNodesForCardAsync(cardNumber, currency);

        var bestRoute = matchingRoutes.Count > 0 ? matchingRoutes[0] : null;
        if (bestRoute != null)
        {
            var preferredAcquirer = bestRoute.GetPreferredAcquirer();
            if (preferredAcquirer != null)
            {
                _logger.LogInformation("Routing card {CardLastFour} ({Currency}) to acquirer {AcquirerId} via route {RouteKey}",
                    GetCardLastFour(cardNumber), currency, preferredAcquirer, bestRoute.ToString());
                return preferredAcquirer;
            }
        }

        _logger.LogWarning("No route found for card {CardLastFour} ({Currency}), falling back to simulator",
            GetCardLastFour(cardNumber), currency);
        return "simulator";
    }

    public async Task<AcquirerPaymentResponse> ProcessPaymentAsync(string cardNumber, int expiryMonth, int expiryYear, string currency, int amount, string cvv)
    {
        var acquirerId = await GetAcquirerIdAsync(cardNumber, currency);
        var acquirerGrain = GrainFactory.GetGrain<IAcquirerGrain>(acquirerId);

        var acquirerRequest = new AcquirerPaymentRequest
        {
            CardNumber = cardNumber,
            ExpiryDate = $"{expiryMonth:D2}/{expiryYear}",
            Currency = currency,
            Amount = amount,
            Cvv = cvv
        };

        return await acquirerGrain.ProcessPaymentAsync(acquirerRequest, CancellationToken.None);
    }

    public async Task RegisterAcquirerAsync(string acquirerId, string routeKey, bool isDefault = false)
    {
        if (string.IsNullOrWhiteSpace(acquirerId))
            throw new ArgumentException("Acquirer ID cannot be null or whitespace", nameof(acquirerId));

        if (string.IsNullOrEmpty(routeKey))
            throw new ArgumentNullException(nameof(routeKey));

        var routeNode = _state.State.GetOrCreateRouteNode(routeKey);
        routeNode.AddAcquirer(acquirerId, isDefault);

        await _state.WriteStateAsync();

        _logger.LogInformation("Registered acquirer {AcquirerId} for route {RouteKey} (default: {IsDefault})",
            acquirerId, routeKey, isDefault);
    }

    public Task<RouteNode?> GetRouteNodeAsync(string routeKey)
    {
        if (string.IsNullOrEmpty(routeKey))
            throw new ArgumentNullException(nameof(routeKey));

        var node = _state.State.GetRouteNode(routeKey);
        return Task.FromResult(node);
    }

    public Task<IReadOnlyList<RouteNode>> GetAllRouteNodesAsync()
    {
        var nodes = _state.State.Routes.Values.ToList();
        return Task.FromResult<IReadOnlyList<RouteNode>>(nodes);
    }

    public Task<IReadOnlyList<RouteNode>> GetRouteNodesForCardAsync(string cardNumber, string currency)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            throw new ArgumentException("Card number cannot be null or whitespace", nameof(cardNumber));

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be null or whitespace", nameof(currency));

        var matchingNodes = _state.State.GetMatchingRoutes(cardNumber, currency);
        return Task.FromResult(matchingNodes);
    }

    public async Task SetDefaultAcquirerAsync(string routeKey, string acquirerId)
    {
        if (string.IsNullOrEmpty(routeKey))
            throw new ArgumentNullException(nameof(routeKey));

        if (string.IsNullOrWhiteSpace(acquirerId))
            throw new ArgumentException("Acquirer ID cannot be null or whitespace", nameof(acquirerId));

        var routeNode = _state.State.GetRouteNode(routeKey);
        if (routeNode != null && routeNode.AcquirerIds.Contains(acquirerId))
        {
            routeNode.DefaultAcquirerId = acquirerId;
            await _state.WriteStateAsync();

            _logger.LogInformation("Set default acquirer {AcquirerId} for route {RouteKey}",
                acquirerId, routeKey);
        }
        else
        {
            _logger.LogWarning("Cannot set default acquirer {AcquirerId} for route {RouteKey} - acquirer not registered",
                acquirerId, routeKey);
        }
    }

    public Task<bool> IsAcquirerRegisteredAsync(string acquirerId, string routeKey)
    {
        if (string.IsNullOrWhiteSpace(acquirerId) || string.IsNullOrEmpty(routeKey))
            return Task.FromResult(false);

        var routeNode = _state.State.GetRouteNode(routeKey);
        var isRegistered = routeNode?.AcquirerIds.Contains(acquirerId) ?? false;
        return Task.FromResult(isRegistered);
    }

    private static string GetCardLastFour(string cardNumber)
    {
        return cardNumber.Length >= 4 ? cardNumber[^4..] : cardNumber;
    }
}