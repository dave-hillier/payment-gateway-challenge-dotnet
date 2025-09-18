using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Models.Acquirer;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController(PaymentsRepository paymentsRepository, CardValidationService cardValidationService, IAcquirerClient acquirerClient) : Controller
{
    [HttpGet("{id:guid}")]
    [Produces("application/vnd.paymentgateway.payment+json", "application/json")]
    [ProducesResponseType(typeof(GetPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPaymentResponse?>> GetPaymentAsync(Guid id)
    {
        var payment = paymentsRepository.Get(id);

        if (payment == null)
        {
            return NotFound();
        }

        return Ok(payment);
    }

    [HttpPost]
    [Consumes("application/vnd.paymentgateway.payment-request+json", "application/json")]
    [Produces("application/vnd.paymentgateway.payment-response+json", "application/json")]
    [ProducesResponseType(typeof(PostPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PostPaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync([FromBody] PostPaymentRequest request)
    {
        if (!ModelState.IsValid)
        {
            // Return a rejected payment response for validation failures
            var rejectedResponse = new PostPaymentResponse
            {
                Id = Guid.NewGuid(),
                Status = PaymentStatus.Rejected,
                CardNumberLastFour = request?.CardNumber?.Length >= 4
                    ? request.CardNumber.Substring(request.CardNumber.Length - 4)
                    : "0000",
                ExpiryMonth = request?.ExpiryMonth ?? 0,
                ExpiryYear = request?.ExpiryYear ?? 0,
                Currency = request?.Currency ?? string.Empty,
                Amount = request?.Amount ?? 0
            };

            return BadRequest(rejectedResponse);
        }

        // Additional business validation using CardValidationService
        if (!cardValidationService.IsValidCardNumber(request.CardNumber) ||
            !cardValidationService.IsValidExpiryDate(request.ExpiryMonth, request.ExpiryYear) ||
            !cardValidationService.IsValidCvv(request.Cvv) ||
            !cardValidationService.IsValidCurrency(request.Currency))
        {
            var rejectedResponse = new PostPaymentResponse
            {
                Id = Guid.NewGuid(),
                Status = PaymentStatus.Rejected,
                CardNumberLastFour = request.CardNumber?.Length >= 4
                    ? request.CardNumber.Substring(request.CardNumber.Length - 4)
                    : "0000",
                ExpiryMonth = request.ExpiryMonth,
                ExpiryYear = request.ExpiryYear,
                Currency = request.Currency,
                Amount = request.Amount
            };

            return BadRequest(rejectedResponse);
        }

        // Create acquiring bank request
        var bankRequest = new AcquirerPaymentRequest
        {
            CardNumber = request.CardNumber,
            ExpiryDate = $"{request.ExpiryMonth:D2}/{request.ExpiryYear}",
            Currency = request.Currency,
            Amount = request.Amount,
            Cvv = request.Cvv
        };

        // Call acquiring bank
        var bankResponse = await acquirerClient.ProcessPaymentAsync(bankRequest);

        // Create payment response based on bank result
        var paymentResponse = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
            CardNumberLastFour = request.CardNumber.Substring(request.CardNumber.Length - 4),
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency,
            Amount = request.Amount
        };

        // Store the payment for retrieval
        paymentsRepository.Add(paymentResponse);

        return Ok(paymentResponse);
    }
}