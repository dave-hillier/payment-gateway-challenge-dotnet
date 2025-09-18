using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository
{
    private readonly List<PostPaymentResponse> _payments = new();
    
    public void Add(PostPaymentResponse payment)
    {
        _payments.Add(payment);
    }

    public PostPaymentResponse? Get(Guid id)
    {
        return _payments.FirstOrDefault(p => p.Id == id);
    }
}