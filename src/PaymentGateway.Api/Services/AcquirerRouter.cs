using PaymentGateway.Api.Grains;

namespace PaymentGateway.Api.Services;

public class AcquirerRouter
{
    private readonly IClusterClient _clusterClient;

    public AcquirerRouter(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    public IAcquirerGrain GetAcquirerGrain(string cardNumber)
    {
        return _clusterClient.GetGrain<IAcquirerGrain>("simulator");
    }

    public (string BaseUrl, TimeSpan Timeout) GetAcquirerConfiguration(string acquirerId)
    {
        return ("http://localhost:8080", TimeSpan.FromSeconds(30));
    }
}