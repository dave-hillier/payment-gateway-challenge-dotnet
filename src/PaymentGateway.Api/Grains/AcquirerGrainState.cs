namespace PaymentGateway.Api.Grains;

[GenerateSerializer]
public class AcquirerGrainState
{
    [Id(0)]
    public string BaseUrl { get; set; } = string.Empty;

    [Id(1)]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

