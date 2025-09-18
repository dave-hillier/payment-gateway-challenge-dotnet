namespace PaymentGateway.Api.Enums;

public enum PaymentStatus
{
    Received,
    Validated,
    Processing,
    Authorized,
    Declined,
    Rejected,
    Failed
}