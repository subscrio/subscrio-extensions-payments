namespace Subscrio.Payments.DTOs;

public sealed class PaymentFilters
{
    public long? CustomerId { get; init; }
    public long? SubscriptionId { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string SortOrder { get; init; } = "desc";
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
}
