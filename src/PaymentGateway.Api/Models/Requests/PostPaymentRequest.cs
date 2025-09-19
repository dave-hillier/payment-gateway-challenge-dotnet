using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Models.Requests;

public record PostPaymentRequest
{
    [Required]
    [StringLength(19, MinimumLength = 14)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "Card number must only contain numeric characters")]
    public string CardNumber { get; set; } = "";

    [Required]
    [Range(1, 12)]
    public int ExpiryMonth { get; set; }

    [Required]
    public int ExpiryYear { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code")]
    public string Currency { get; set; } = "";

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Amount must be a positive integer")]
    public int Amount { get; set; }

    [Required]
    [StringLength(4, MinimumLength = 3)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "CVV must only contain numeric characters")]
    public string Cvv { get; set; } = "";
}