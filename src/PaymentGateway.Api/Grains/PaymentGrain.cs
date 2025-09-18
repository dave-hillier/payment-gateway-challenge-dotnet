using Orleans;
using Orleans.Runtime;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Grains;

public class PaymentGrain : Grain, IPaymentGrain
{
    private readonly IPersistentState<PaymentState> _state;
    private readonly ILogger<PaymentGrain> _logger;
    private readonly CardValidationService _cardValidationService;
    private readonly IAcquirerClient _acquirerClient;
    private IDisposable? _retryTimer;

    public PaymentGrain(
        [PersistentState("paymentState", "paymentStore")] IPersistentState<PaymentState> state,
        ILogger<PaymentGrain> logger,
        CardValidationService cardValidationService,
        IAcquirerClient acquirerClient)
    {
        _state = state;
        _logger = logger;
        _cardValidationService = cardValidationService;
        _acquirerClient = acquirerClient;
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
            ? cardNumber.Substring(cardNumber.Length - 4)
            : "0000";
        _state.State.ExpiryMonth = expiryMonth;
        _state.State.ExpiryYear = expiryYear;
        _state.State.Currency = currency;
        _state.State.Amount = amount;
        _state.State.CVV = cvv;
        _state.State.CreatedAt = DateTime.UtcNow;
        _state.State.RetryCount = 0;

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

            var bankResponse = await _acquirerClient.ProcessPaymentAsync(
                _state.State.CardNumber,
                _state.State.ExpiryMonth,
                _state.State.ExpiryYear,
                _state.State.Currency,
                _state.State.Amount,
                _state.State.CVV,
                CancellationToken.None);

            _state.State.Status = bankResponse.Status switch
            {
                PaymentStatus.Authorized => PaymentStatus.Authorized,
                PaymentStatus.Declined => PaymentStatus.Declined,
                _ => PaymentStatus.Rejected
            };
            _state.State.ProcessedAt = DateTime.UtcNow;
            _state.State.BankResponseCode = bankResponse.AuthorizationCode;

            await _state.WriteStateAsync();

            _logger.LogInformation("Payment {PaymentId} processed with status {Status}",
                _state.State.Id, _state.State.Status);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogError(ex, "Bank service unavailable for payment {PaymentId}", _state.State.Id);
            _state.State.Status = PaymentStatus.Failed;
            _state.State.ProcessedAt = DateTime.UtcNow;
            _state.State.BankResponseCode = "SERVICE_UNAVAILABLE";
            await _state.WriteStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment {PaymentId}", _state.State.Id);
            await HandleRetry();
        }
    }

    private async Task HandleRetry()
    {
        _state.State.RetryCount++;

        if (_state.State.RetryCount >= 3)
        {
            _state.State.Status = PaymentStatus.Failed;
            _state.State.ProcessedAt = DateTime.UtcNow;
            await _state.WriteStateAsync();
            _logger.LogWarning("Payment {PaymentId} failed after {RetryCount} retries",
                _state.State.Id, _state.State.RetryCount);
        }
        else
        {
            _state.State.Status = PaymentStatus.Validated;
            await _state.WriteStateAsync();

            var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, _state.State.RetryCount));
            _logger.LogInformation("Payment {PaymentId} scheduled for retry #{RetryCount} in {Delay}",
                _state.State.Id, _state.State.RetryCount, retryDelay);

            // Use Orleans timer for retry
            _retryTimer = this.RegisterGrainTimer(async () => await ProcessWithBankAsync(), new() { DueTime = retryDelay, Period = TimeSpan.MaxValue, Interleave = true });
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

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _retryTimer?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }
}