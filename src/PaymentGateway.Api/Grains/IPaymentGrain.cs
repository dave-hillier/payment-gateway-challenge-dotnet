using Orleans;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Grains;

public interface IPaymentGrain : IGrainWithStringKey
{
    Task<PostPaymentResponse> ProcessPaymentAsync(
        string cardNumber,
        int expiryMonth,
        int expiryYear,
        string currency,
        int amount,
        string cvv);

    Task<GetPaymentResponse?> GetPaymentAsync();
}