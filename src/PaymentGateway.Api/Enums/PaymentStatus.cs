namespace PaymentGateway.Api.Enums;

public enum PaymentStatus
{
    None,
    Validated,
    Processing,
    Authorized,
    Declined,
    Rejected,
    Failed
}