namespace Subscrio.Payments.DTOs;

public sealed class PaymentDto
{
    public long Id { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    public int AmountPaid { get; init; }
    public string Currency { get; init; } = string.Empty;
    public long? CustomerId { get; init; }
    public long? SubscriptionId { get; init; }
    public long? BillingCycleId { get; init; }
    public string? ExternalProductId { get; init; }
    public int? DurationValue { get; init; }
    public string? DurationUnit { get; init; }
    public string StripeInvoiceId { get; init; } = string.Empty;
    public string? StripeEventId { get; init; }
}
