using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests.TestHelpers;

public class PaymentRequestBuilder
{
    private string _cardNumber = "4111111111111111";
    private int _expiryMonth = 12;
    private int _expiryYear = 2026;
    private string _currency = "USD";
    private int _amount = 1000;
    private string _cvv = "123";

    public PaymentRequestBuilder WithCardNumber(string cardNumber)
    {
        _cardNumber = cardNumber;
        return this;
    }

    public PaymentRequestBuilder WithOddCardNumber()
    {
        _cardNumber = "4111111111111111"; // Ends in 1 (odd) - should be authorized
        return this;
    }

    public PaymentRequestBuilder WithEvenCardNumber()
    {
        _cardNumber = "4000000000000002"; // Ends in 2 (even) - should be declined
        return this;
    }

    public PaymentRequestBuilder WithBankFailureCard()
    {
        _cardNumber = "4000000000000010"; // Ends in 0 - causes bank failure
        return this;
    }

    public PaymentRequestBuilder WithInvalidCardNumber()
    {
        _cardNumber = "123"; // Invalid card number
        return this;
    }

    public PaymentRequestBuilder WithExpiry(int month, int year)
    {
        _expiryMonth = month;
        _expiryYear = year;
        return this;
    }

    public PaymentRequestBuilder WithPastExpiry()
    {
        _expiryMonth = 12;
        _expiryYear = 2020; // Past year
        return this;
    }

    public PaymentRequestBuilder WithCurrency(string currency)
    {
        _currency = currency;
        return this;
    }

    public PaymentRequestBuilder WithAmount(int amount)
    {
        _amount = amount;
        return this;
    }

    public PaymentRequestBuilder WithCvv(string cvv)
    {
        _cvv = cvv;
        return this;
    }

    public PostPaymentRequest Build()
    {
        return new PostPaymentRequest
        {
            CardNumber = _cardNumber,
            ExpiryMonth = _expiryMonth,
            ExpiryYear = _expiryYear,
            Currency = _currency,
            Amount = _amount,
            Cvv = _cvv
        };
    }

    public static PaymentRequestBuilder Create() => new();
}