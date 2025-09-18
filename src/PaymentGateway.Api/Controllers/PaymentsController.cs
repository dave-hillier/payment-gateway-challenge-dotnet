using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Data.Entities;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController(
    PaymentGatewayDbContext dbContext,
    CardValidationService cardValidationService,
    IPaymentCompletionService completionService) : Controller
{
    [HttpGet("{id:guid}")]
    [Produces("application/vnd.paymentgateway.payment+json", "application/json")]
    [ProducesResponseType(typeof(GetPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPaymentResponse?>> GetPaymentAsync(Guid id)
    {
        var payment = await dbContext.PaymentRequests
            .FirstOrDefaultAsync(p => p.Id == id);

        if (payment == null)
        {
            return NotFound();
        }

        var response = new GetPaymentResponse
        {
            Id = payment.Id,
            Status = payment.Status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        };

        return Ok(response);
    }

    [HttpPost]
    [Consumes("application/vnd.paymentgateway.payment-request+json", "application/json")]
    [Produces("application/vnd.paymentgateway.payment-response+json", "application/json")]
    [ProducesResponseType(typeof(PostPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PostPaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
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

        // Get idempotency key from headers if present
        var idempotencyKey = Request.Headers["Cko-Idempotency-Key"].FirstOrDefault();

        // Check for existing payment with same idempotency key
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var existingPayment = await dbContext.PaymentRequests
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);

            if (existingPayment != null)
            {
                // Return existing payment result
                return Ok(new PostPaymentResponse
                {
                    Id = existingPayment.Id,
                    Status = existingPayment.Status,
                    CardNumberLastFour = existingPayment.CardNumberLastFour,
                    ExpiryMonth = existingPayment.ExpiryMonth,
                    ExpiryYear = existingPayment.ExpiryYear,
                    Currency = existingPayment.Currency,
                    Amount = existingPayment.Amount
                });
            }
        }

        // Create payment request entity
        var paymentRequest = new PaymentRequest
        {
            Id = Guid.NewGuid(),
            CardNumber = request.CardNumber,
            CardNumberLastFour = request.CardNumber.Substring(request.CardNumber.Length - 4),
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency,
            Amount = request.Amount,
            CVV = request.Cvv,
            Status = PaymentStatus.Validated,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey,
            RetryCount = 0
        };

        // Store payment request in database (this triggers background processing)
        dbContext.PaymentRequests.Add(paymentRequest);
        await dbContext.SaveChangesAsync();

        // Wait for payment completion using signaling (max 30 seconds)
        var completedPayment = await completionService.WaitForCompletionAsync(
            paymentRequest.Id,
            TimeSpan.FromSeconds(30));

        if (completedPayment != null)
        {
            // Check if payment failed due to bank service unavailable
            if (completedPayment.Status == PaymentStatus.Failed &&
                completedPayment.BankResponseCode == "SERVICE_UNAVAILABLE")
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // Payment has been processed
            var response = new PostPaymentResponse
            {
                Id = completedPayment.Id,
                Status = completedPayment.Status,
                CardNumberLastFour = completedPayment.CardNumberLastFour,
                ExpiryMonth = completedPayment.ExpiryMonth,
                ExpiryYear = completedPayment.ExpiryYear,
                Currency = completedPayment.Currency,
                Amount = completedPayment.Amount
            };

            return Ok(response);
        }

        // Timeout occurred - check database for current status
        var currentPayment = await dbContext.PaymentRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentRequest.Id);

        if (currentPayment != null &&
            (currentPayment.Status == PaymentStatus.Authorized ||
             currentPayment.Status == PaymentStatus.Declined ||
             currentPayment.Status == PaymentStatus.Failed))
        {
            // Check if payment failed due to bank service unavailable
            if (currentPayment.Status == PaymentStatus.Failed &&
                currentPayment.BankResponseCode == "SERVICE_UNAVAILABLE")
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // Payment completed just after timeout
            return Ok(new PostPaymentResponse
            {
                Id = currentPayment.Id,
                Status = currentPayment.Status,
                CardNumberLastFour = currentPayment.CardNumberLastFour,
                ExpiryMonth = currentPayment.ExpiryMonth,
                ExpiryYear = currentPayment.ExpiryYear,
                Currency = currentPayment.Currency,
                Amount = currentPayment.Amount
            });
        }

        // Still processing after timeout
        return StatusCode(StatusCodes.Status504GatewayTimeout, new PostPaymentResponse
        {
            Id = paymentRequest.Id,
            Status = PaymentStatus.Processing,
            CardNumberLastFour = paymentRequest.CardNumberLastFour,
            ExpiryMonth = paymentRequest.ExpiryMonth,
            ExpiryYear = paymentRequest.ExpiryYear,
            Currency = paymentRequest.Currency,
            Amount = paymentRequest.Amount
        });
    }
}