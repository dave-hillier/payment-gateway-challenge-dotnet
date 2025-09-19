using Orleans;
using Orleans.Runtime;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Models.Acquirer;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Grains;

public class PaymentGrain(
    [PersistentState("paymentState", "paymentStore")] IPersistentState<PaymentState> state,
    ILogger<PaymentGrain> logger,
    CardValidationService cardValidationService)
    : Grain, IPaymentGrain
{
    public async Task<PostPaymentResponse> ProcessPaymentAsync(
        string cardNumber,
        int expiryMonth,
        int expiryYear,
        string currency,
        int amount,
        string cvv)
    {
        // If payment already exists, return existing result (idempotency)
        if (state.State.Id != Guid.Empty)
        {
            return CreateResponse();
        }

        // Initialize payment state
        var paymentId = Guid.Parse(this.GetPrimaryKeyString());
        state.State.Id = paymentId;
        state.State.CardNumber = cardNumber;
        state.State.CardNumberLastFour = cardNumber.Length >= 4
            ? cardNumber[^4..]
            : "0000";
        state.State.ExpiryMonth = expiryMonth;
        state.State.ExpiryYear = expiryYear;
        state.State.Currency = currency;
        state.State.Amount = amount;
        state.State.CVV = cvv;
        state.State.CreatedAt = DateTime.UtcNow;

        // Validate payment request
        if (!cardValidationService.IsValidCardNumber(cardNumber) ||
            !cardValidationService.IsValidExpiryDate(expiryMonth, expiryYear) ||
            !cardValidationService.IsValidCvv(cvv) ||
            !cardValidationService.IsValidCurrency(currency))
        {
            state.State.Status = PaymentStatus.Rejected;
            await state.WriteStateAsync();
            logger.LogInformation("Payment {PaymentId} rejected due to validation failure", paymentId);
            return CreateResponse();
        }

        state.State.Status = PaymentStatus.Validated;
        await state.WriteStateAsync();

        // Start processing
        await ProcessWithBankAsync();

        return CreateResponse();
    }

    public Task<GetPaymentResponse?> GetPaymentAsync()
    {
        if (state.State.Id == Guid.Empty)
        {
            return Task.FromResult<GetPaymentResponse?>(null);
        }

        var response = new GetPaymentResponse
        {
            Id = state.State.Id,
            Status = state.State.Status,
            CardNumberLastFour = state.State.CardNumberLastFour,
            ExpiryMonth = state.State.ExpiryMonth,
            ExpiryYear = state.State.ExpiryYear,
            Currency = state.State.Currency,
            Amount = state.State.Amount
        };

        return Task.FromResult<GetPaymentResponse?>(response);
    }

    private async Task ProcessWithBankAsync()
    {
        try
        {
            state.State.Status = PaymentStatus.Processing;
            await state.WriteStateAsync();

            logger.LogInformation("Processing payment {PaymentId} with bank", state.State.Id);

            var paymentRouterGrain = GrainFactory.GetGrain<IPaymentRouterGrain>("global");
            AcquirerPaymentResponse bankResponse = await paymentRouterGrain.ProcessPaymentAsync(
                state.State.CardNumber,
                state.State.ExpiryMonth,
                state.State.ExpiryYear,
                state.State.Currency,
                state.State.Amount,
                state.State.CVV);

            state.State.Status = bankResponse.Authorized switch
            {
                true => PaymentStatus.Authorized,
                false => PaymentStatus.Declined
            };
            state.State.ProcessedAt = DateTime.UtcNow;
            state.State.BankResponseCode = bankResponse.AuthorizationCode ?? "NO_CODE";

            await state.WriteStateAsync();

            logger.LogInformation("Payment {PaymentId} processed with status {Status}",
                state.State.Id, state.State.Status);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            logger.LogError(ex, "All acquirers unavailable for payment {PaymentId}", state.State.Id);

            state.State.Status = PaymentStatus.Failed;
            state.State.ProcessedAt = DateTime.UtcNow;
            state.State.BankResponseCode = "SERVICE_UNAVAILABLE";
            await state.WriteStateAsync();

            // Re-throw to signal service unavailable to controller
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing payment {PaymentId}", state.State.Id);

            state.State.Status = PaymentStatus.Failed;
            state.State.ProcessedAt = DateTime.UtcNow;
            state.State.BankResponseCode = "PROCESSING_ERROR";
            await state.WriteStateAsync();

            throw;
        }
    }

    private PostPaymentResponse CreateResponse()
    {
        return new PostPaymentResponse
        {
            Id = state.State.Id,
            Status = state.State.Status,
            CardNumberLastFour = state.State.CardNumberLastFour,
            ExpiryMonth = state.State.ExpiryMonth,
            ExpiryYear = state.State.ExpiryYear,
            Currency = state.State.Currency,
            Amount = state.State.Amount
        };
    }

}