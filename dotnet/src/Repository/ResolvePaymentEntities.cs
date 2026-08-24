using Npgsql;

namespace Subscrio.Payments.Repository;

public sealed class PaymentEntityAssociation
{
    public required long CustomerId { get; init; }
    public required long SubscriptionId { get; init; }
    public long? BillingCycleId { get; init; }
    public string? ExternalProductId { get; init; }
    public int? DurationValue { get; init; }
    public string? DurationUnit { get; init; }
}

/// <summary>
/// Resolve Subscrio subscription, customer, and billing cycle from a Stripe subscription ID.
/// </summary>
public static class ResolvePaymentEntities
{
    public static async Task<PaymentEntityAssociation?> ResolveAsync(
        NpgsqlDataSource dataSource,
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
              s.id AS subscription_id,
              s.customer_id,
              s.billing_cycle_id,
              bc.external_product_id,
              bc.duration_value,
              bc.duration_unit
            FROM subscrio.subscriptions s
            LEFT JOIN subscrio.billing_cycles bc ON bc.id = s.billing_cycle_id
            WHERE s.stripe_subscription_id = @stripeSubId
            LIMIT 1
            """,
            conn);
        cmd.Parameters.AddWithValue("stripeSubId", stripeSubscriptionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new PaymentEntityAssociation
        {
            SubscriptionId = reader.GetInt64(0),
            CustomerId = reader.GetInt64(1),
            BillingCycleId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
            ExternalProductId = reader.IsDBNull(3) ? null : reader.GetString(3),
            DurationValue = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            DurationUnit = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }
}
