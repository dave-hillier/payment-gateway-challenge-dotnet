using Orleans;

using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Grains;

public interface IAcquirerGrain : IGrainWithStringKey
{
    Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default);

    Task ConfigureAsync(string baseUrl, TimeSpan timeout);

    Task RegisterForRouteAsync(string routeKey, bool isDefault = false);
}


