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
   
        await jpyAcquirerGrain.RegisterForRouteAsync("*/JPY", true);
        await eurAcquirerGrain.RegisterForRouteAsync("5*/EUR", true);
        await simulatorGrain.RegisterForRouteAsync("*/*", true); 
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

}