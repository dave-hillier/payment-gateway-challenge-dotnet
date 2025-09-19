using Orleans;

using PaymentGateway.Api.Grains;

namespace PaymentGateway.Api.Services;

public class AcquirerRouteRegistrationService(IClusterClient clusterClient) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var simulatorGrain = clusterClient.GetGrain<IAcquirerGrain>("simulator");
        var jpyAcquirerGrain = clusterClient.GetGrain<IAcquirerGrain>("jpy-acquirer");
        var eurAcquirerGrain = clusterClient.GetGrain<IAcquirerGrain>("eur-acquirer");
   
        // Register routing for JPY - demonstrating the new routing system
        await jpyAcquirerGrain.RegisterForRouteAsync("5*/JPY", true);  // Mastercard JPY -> jpy-acquirer
        await jpyAcquirerGrain.RegisterForRouteAsync("4*/JPY", false); // Visa JPY -> jpy-acquirer (not default)
        await jpyAcquirerGrain.RegisterForRouteAsync("*/JPY", true);   // Any card JPY -> jpy-acquirer (fallback)

        // Register routing for EUR
        await eurAcquirerGrain.RegisterForRouteAsync("5*/EUR", true);  // Mastercard EUR -> eur-acquirer
        await eurAcquirerGrain.RegisterForRouteAsync("*/EUR", true);   // Any card EUR -> eur-acquirer (fallback)

        // Register simulator as fallback for everything else
        await simulatorGrain.RegisterForRouteAsync("*/*", true);       // Global fallback -> simulator
        await simulatorGrain.RegisterForRouteAsync("4*/USD", true);    // Visa USD -> simulator
        await simulatorGrain.RegisterForRouteAsync("5*/USD", true);    // Mastercard USD -> simulator
        await simulatorGrain.RegisterForRouteAsync("*/USD", true);     // Any USD -> simulator
        await simulatorGrain.RegisterForRouteAsync("*/GBP", true);     // Any GBP -> simulator
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

}