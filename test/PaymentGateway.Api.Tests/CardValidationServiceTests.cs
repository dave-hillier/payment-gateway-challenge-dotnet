using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class CardValidationServiceTests
{
    private readonly CardValidationService _cardValidationService = new();

    [Theory]
    [InlineData("4111111111111111", true)]  // Valid Visa test card
    [InlineData("4000000000000002", true)]  // Valid Visa test card
    [InlineData("5555555555554444", true)]  // Valid Mastercard test card
    [InlineData("371449635398431", true)]   // Valid Amex test card
    [InlineData("4111111111111112", false)] // Invalid Luhn
    [InlineData("4111111111111110", false)] // Invalid Luhn
    [InlineData("123", false)]              // Too short
    [InlineData("12345678901234567890", false)] // Too long
    [InlineData("411111111111111a", false)] // Contains letter
    [InlineData("", false)]                 // Empty
    [InlineData(null, false)]               // Null
    public void IsValidCardNumber_ShouldValidateCorrectly(string cardNumber, bool expected)
    {
        var result = _cardValidationService.IsValidCardNumber(cardNumber);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(12, 2026, true)]   // Future date
    [InlineData(12, 2030, true)]   // Future date
    [InlineData(1, 2026, true)]    // Future date
    [InlineData(12, 2020, false)]  // Past year
    [InlineData(1, 2020, false)]   // Past year
    [InlineData(0, 2026, false)]   // Invalid month
    [InlineData(13, 2026, false)]  // Invalid month
    [InlineData(-1, 2026, false)]  // Invalid month
    public void IsValidExpiryDate_ShouldValidateCorrectly(int month, int year, bool expected)
    {
        var result = _cardValidationService.IsValidExpiryDate(month, year);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsValidExpiryDate_CurrentMonthAndYear_ShouldReturnTrue()
    {
        var now = DateTime.UtcNow;
        var result = _cardValidationService.IsValidExpiryDate(now.Month, now.Year);
        Assert.True(result);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("1234", true)]
    [InlineData("456", true)]
    [InlineData("12", false)]  // Too short
    [InlineData("12345", false)] // Too long
    [InlineData("12a", false)] // Contains letter
    [InlineData("", false)]    // Empty
    [InlineData(null, false)]  // Null
    public void IsValidCvv_ShouldValidateCorrectly(string cvv, bool expected)
    {
        var result = _cardValidationService.IsValidCvv(cvv);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("GBP", true)]
    [InlineData("EUR", true)]
    [InlineData("usd", true)]  // Case insensitive
    [InlineData("gbp", true)]  // Case insensitive
    [InlineData("eur", true)]  // Case insensitive
    [InlineData("JPY", false)] // Not supported
    [InlineData("CAD", false)] // Not supported
    [InlineData("", false)]    // Empty
    [InlineData(null, false)]  // Null
    public void IsValidCurrency_ShouldValidateCorrectly(string currency, bool expected)
    {
        var result = _cardValidationService.IsValidCurrency(currency);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4000000000000002")]
    [InlineData("5555555555554444")]
    [InlineData("371449635398431")]
    public void IsValidCardNumber_WithValidLuhnCards_ShouldReturnTrue(string cardNumber)
    {
        var result = _cardValidationService.IsValidCardNumber(cardNumber);
        Assert.True(result);
    }

    [Theory]
    [InlineData("4111111111111112")]
    [InlineData("4111111111111110")]
    [InlineData("5555555555554443")]
    [InlineData("371449635398432")]
    public void IsValidCardNumber_WithInvalidLuhnCards_ShouldReturnFalse(string cardNumber)
    {
        var result = _cardValidationService.IsValidCardNumber(cardNumber);
        Assert.False(result);
    }
}