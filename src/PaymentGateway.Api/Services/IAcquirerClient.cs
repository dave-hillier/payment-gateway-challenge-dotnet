using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Services;

public interface IAcquirerClient
{
    Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default);
}