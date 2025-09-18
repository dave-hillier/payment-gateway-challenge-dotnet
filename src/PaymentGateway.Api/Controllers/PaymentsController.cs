using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Grains;
using Orleans;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController(
    IClusterClient clusterClient) : Controller
{
    [HttpGet("{id:guid}")]
    [Produces("application/vnd.paymentgateway.payment+json", "application/json")]
    [ProducesResponseType(typeof(GetPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPaymentResponse?>> GetPaymentAsync(Guid id)
    {
        var paymentGrain = clusterClient.GetGrain<IPaymentGrain>(id.ToString());
        var payment = await paymentGrain.GetPaymentAsync();

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
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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

        // Get idempotency key from headers - use as grain key for natural idempotency
        var idempotencyKey = Request.Headers["Cko-Idempotency-Key"].FirstOrDefault();
        var grainKey = !string.IsNullOrEmpty(idempotencyKey) ? idempotencyKey : Guid.NewGuid().ToString();

        // Get the payment grain (idempotent by grain identity)
        var paymentGrain = clusterClient.GetGrain<IPaymentGrain>(grainKey);

        // Process payment through grain
        var response = await paymentGrain.ProcessPaymentAsync(
            request.CardNumber,
            request.ExpiryMonth,
            request.ExpiryYear,
            request.Currency,
            request.Amount,
            request.Cvv);

        // Check if payment failed due to bank service unavailable
        if (response.Status == PaymentStatus.Failed)
        {
            // Get the payment details to check bank response code
            var paymentDetails = await paymentGrain.GetPaymentAsync();
            if (paymentDetails != null &&
                paymentDetails.Status == PaymentStatus.Failed)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }

        // Check if payment was rejected due to validation failures
        if (response.Status == PaymentStatus.Rejected)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}