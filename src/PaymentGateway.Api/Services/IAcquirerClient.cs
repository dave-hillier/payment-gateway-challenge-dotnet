using PaymentGateway.Api.Models.Acquirer;

namespace PaymentGateway.Api.Services;

public interface IAcquirerClient
{
    Task<AcquirerPaymentResponse> ProcessPaymentAsync(AcquirerPaymentRequest request, CancellationToken cancellationToken = default);

    Task<AcquirerPaymentResponse> ProcessPaymentAsync(
        string cardNumber,
        int expiryMonth,
        int expiryYear,
        string currency,
        int amount,
        string cvv,
        CancellationToken cancellationToken = default);
}