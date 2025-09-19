using Orleans;
using Orleans.Runtime;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Models.Acquirer;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Grains;

public class PaymentGrain : Grain, IPaymentGrain
{
    private readonly IPersistentState<PaymentState> _state;
    private readonly ILogger<PaymentGrain> _logger;
    private readonly CardValidationService _cardValidationService;

    public PaymentGrain(
        [PersistentState("paymentState", "paymentStore")] IPersistentState<PaymentState> state,
        ILogger<PaymentGrain> logger,
        CardValidationService cardValidationService)
    {
        _state = state;
        _logger = logger;
        _cardValidationService = cardValidationService;
    }

    public async Task<PostPaymentResponse> ProcessPaymentAsync(
        string cardNumber,
        int expiryMonth,
        int expiryYear,
        string currency,
        int amount,
        string cvv)
    {
        // If payment already exists, return existing result (idempotency)
        if (_state.State.Id != Guid.Empty)
        {
            return CreateResponse();
        }

        // Initialize payment state
        var paymentId = Guid.Parse(this.GetPrimaryKeyString());
        _state.State.Id = paymentId;
        _state.State.CardNumber = cardNumber;
        _state.State.CardNumberLastFour = cardNumber.Length >= 4
            ? cardNumber[^4..]
            : "0000";
        _state.State.ExpiryMonth = expiryMonth;
        _state.State.ExpiryYear = expiryYear;
        _state.State.Currency = currency;
        _state.State.Amount = amount;
        _state.State.CVV = cvv;
        _state.State.CreatedAt = DateTime.UtcNow;

        // Validate payment request
        if (!_cardValidationService.IsValidCardNumber(cardNumber) ||
            !_cardValidationService.IsValidExpiryDate(expiryMonth, expiryYear) ||
            !_cardValidationService.IsValidCvv(cvv) ||
            !_cardValidationService.IsValidCurrency(currency))
        {
            _state.State.Status = PaymentStatus.Rejected;
            await _state.WriteStateAsync();
            _logger.LogInformation("Payment {PaymentId} rejected due to validation failure", paymentId);
            return CreateResponse();
        }

        _state.State.Status = PaymentStatus.Validated;
        await _state.WriteStateAsync();

        // Start processing
        await ProcessWithBankAsync();

        return CreateResponse();
    }

    public Task<GetPaymentResponse?> GetPaymentAsync()
    {
        if (_state.State.Id == Guid.Empty)
        {
            return Task.FromResult<GetPaymentResponse?>(null);
        }

        var response = new GetPaymentResponse
        {
            Id = _state.State.Id,
            Status = _state.State.Status,
            CardNumberLastFour = _state.State.CardNumberLastFour,
            ExpiryMonth = _state.State.ExpiryMonth,
            ExpiryYear = _state.State.ExpiryYear,
            Currency = _state.State.Currency,
            Amount = _state.State.Amount
        };

        return Task.FromResult<GetPaymentResponse?>(response);
    }

    private async Task ProcessWithBankAsync()
    {
        try
        {
            _state.State.Status = PaymentStatus.Processing;
            await _state.WriteStateAsync();

            _logger.LogInformation("Processing payment {PaymentId} with bank", _state.State.Id);

            var paymentRouterGrain = GrainFactory.GetGrain<IPaymentRouterGrain>("global");
            AcquirerPaymentResponse bankResponse = await paymentRouterGrain.ProcessPaymentAsync(
                _state.State.CardNumber,
                _state.State.ExpiryMonth,
                _state.State.ExpiryYear,
                _state.State.Currency,
                _state.State.Amount,
                _state.State.CVV);

            _state.State.Status = bankResponse.Authorized switch
            {
                true => PaymentStatus.Authorized,
                false => PaymentStatus.Declined
            };
            _state.State.ProcessedAt = DateTime.UtcNow;
            _state.State.BankResponseCode = bankResponse.AuthorizationCode ?? "NO_CODE";

            await _state.WriteStateAsync();

            _logger.LogInformation("Payment {PaymentId} processed with status {Status}",
                _state.State.Id, _state.State.Status);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogError(ex, "All acquirers unavailable for payment {PaymentId}", _state.State.Id);

            _state.State.Status = PaymentStatus.Failed;
            _state.State.ProcessedAt = DateTime.UtcNow;
            _state.State.BankResponseCode = "SERVICE_UNAVAILABLE";
            await _state.WriteStateAsync();

            // Re-throw to signal service unavailable to controller
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment {PaymentId}", _state.State.Id);

            _state.State.Status = PaymentStatus.Failed;
            _state.State.ProcessedAt = DateTime.UtcNow;
            _state.State.BankResponseCode = "PROCESSING_ERROR";
            await _state.WriteStateAsync();

            throw;
        }
    }

    private PostPaymentResponse CreateResponse()
    {
        return new PostPaymentResponse
        {
            Id = _state.State.Id,
            Status = _state.State.Status,
            CardNumberLastFour = _state.State.CardNumberLastFour,
            ExpiryMonth = _state.State.ExpiryMonth,
            ExpiryYear = _state.State.ExpiryYear,
            Currency = _state.State.Currency,
            Amount = _state.State.Amount
        };
    }

}