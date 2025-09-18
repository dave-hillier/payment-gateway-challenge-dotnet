using System;

namespace PaymentGateway.Api.Services;

public class CardValidationService
{
    private static readonly HashSet<string> SupportedCurrencies = new() { "USD", "GBP", "EUR" };

    public bool IsValidCardNumber(string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber) || cardNumber.Length < 14 || cardNumber.Length > 19)
        {
            return false;
        }

        if (!cardNumber.All(char.IsDigit))
        {
            return false;
        }

        return IsValidLuhn(cardNumber);
    }

    public bool IsValidExpiryDate(int month, int year)
    {
        if (month < 1 || month > 12)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var expiryDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));

        return expiryDate > now;
    }

    public bool IsValidCvv(string cvv)
    {
        if (string.IsNullOrWhiteSpace(cvv))
        {
            return false;
        }

        return cvv.Length >= 3 && cvv.Length <= 4 && cvv.All(char.IsDigit);
    }

    public bool IsValidCurrency(string currency)
    {
        return !string.IsNullOrWhiteSpace(currency) && SupportedCurrencies.Contains(currency.ToUpperInvariant());
    }

    private static bool IsValidLuhn(string cardNumber)
    {
        int sum = 0;
        bool alternate = false;

        for (int i = cardNumber.Length - 1; i >= 0; i--)
        {
            int digit = cardNumber[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit = (digit % 10) + 1;
                }
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }
}