namespace PaymentGateway.Api.Enums;

public enum PaymentStatus
{
    None,
    Received,
    Validated,
    Processing,
    Authorized,
    Declined,
    Rejected,
    Failed
}