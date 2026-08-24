namespace Subscrio.Payments;

/// <summary>
/// Configuration for the payments extension.
/// </summary>
public sealed class PaymentTrackerOptions
{
    /// <summary>
    /// PostgreSQL connection string (typically the same database as Subscrio.Core).
    /// </summary>
    public required string ConnectionString { get; init; }
}
