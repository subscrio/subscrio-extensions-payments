using Npgsql;
using Subscrio.Payments.DTOs;

namespace Subscrio.Payments.Repository;

public sealed class InsertPaymentInput
{
    public required int AmountPaid { get; init; }
    public required string Currency { get; init; }
    public long? CustomerId { get; init; }
    public long? SubscriptionId { get; init; }
    public long? BillingCycleId { get; init; }
    public string? ExternalProductId { get; init; }
    public int? DurationValue { get; init; }
    public string? DurationUnit { get; init; }
    public required string StripeInvoiceId { get; init; }
    public string? StripeEventId { get; init; }
}

public sealed class PostgresPaymentRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPaymentRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task InsertAsync(InsertPaymentInput input, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO subscrio.payments (
              amount_paid, currency, customer_id, subscription_id, billing_cycle_id,
              external_product_id, duration_value, duration_unit,
              stripe_invoice_id, stripe_event_id
            ) VALUES (
              @amountPaid, @currency, @customerId, @subscriptionId, @billingCycleId,
              @externalProductId, @durationValue, @durationUnit,
              @stripeInvoiceId, @stripeEventId
            )
            ON CONFLICT (stripe_invoice_id) DO NOTHING
            """, connection);

        cmd.Parameters.AddWithValue("amountPaid", input.AmountPaid);
        cmd.Parameters.AddWithValue("currency", input.Currency);
        cmd.Parameters.AddWithValue("customerId", (object?)input.CustomerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("subscriptionId", (object?)input.SubscriptionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("billingCycleId", (object?)input.BillingCycleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("externalProductId", (object?)input.ExternalProductId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("durationValue", (object?)input.DurationValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("durationUnit", (object?)input.DurationUnit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("stripeInvoiceId", input.StripeInvoiceId);
        cmd.Parameters.AddWithValue("stripeEventId", (object?)input.StripeEventId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PaymentDto?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM subscrio.payments WHERE id = @id",
            connection);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return MapRow(reader);
    }

    public async Task<PaymentPage> ListAsync(
        PaymentFilters? filters = null,
        CancellationToken cancellationToken = default)
    {
        var f = NormalizeFilters(filters);

        var where = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        void Add(string clause, string name, object value)
        {
            where.Add(clause);
            parameters.Add(new NpgsqlParameter(name, value));
        }

        if (f.CustomerId.HasValue)
            Add("customer_id = @customerId", "customerId", f.CustomerId.Value);
        if (f.SubscriptionId.HasValue)
            Add("subscription_id = @subscriptionId", "subscriptionId", f.SubscriptionId.Value);
        if (f.StartDate.HasValue)
            Add("created_at >= @startDate", "startDate", f.StartDate.Value.ToUniversalTime());
        if (f.EndDate.HasValue)
            Add("created_at <= @endDate", "endDate", f.EndDate.Value.ToUniversalTime());

        var whereSql = where.Count > 0 ? $"WHERE {string.Join(" AND ", where)}" : "";
        var sortOrder = string.Equals(f.SortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        long total;
        await using (var countCmd = new NpgsqlCommand(
            $"SELECT COUNT(*)::bigint FROM subscrio.payments {whereSql}",
            connection))
        {
            foreach (var p in parameters)
                countCmd.Parameters.Add(CloneParameter(p));
            total = (long)(await countCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        var data = new List<PaymentDto>();
        await using (var dataCmd = new NpgsqlCommand($"""
            SELECT * FROM subscrio.payments
            {whereSql}
            ORDER BY created_at {sortOrder}, id {sortOrder}
            LIMIT @limit OFFSET @offset
            """, connection))
        {
            foreach (var p in parameters)
                dataCmd.Parameters.Add(CloneParameter(p));
            dataCmd.Parameters.AddWithValue("limit", f.Limit);
            dataCmd.Parameters.AddWithValue("offset", f.Offset);

            await using var reader = await dataCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                data.Add(MapRow(reader));
        }

        return new PaymentPage { Data = data, Total = total };
    }

    private static PaymentFilters NormalizeFilters(PaymentFilters? filters)
    {
        filters ??= new PaymentFilters();

        var sortOrder = string.IsNullOrWhiteSpace(filters.SortOrder) ? "desc" : filters.SortOrder.ToLowerInvariant();
        if (sortOrder is not ("asc" or "desc"))
            throw new ArgumentException($"Invalid sortOrder: {filters.SortOrder}");

        var limit = filters.Limit <= 0 ? 50 : filters.Limit;
        if (limit is < 1 or > 500)
            throw new ArgumentException("limit must be between 1 and 500");

        var offset = filters.Offset < 0
            ? throw new ArgumentException("offset must be >= 0")
            : filters.Offset;

        return new PaymentFilters
        {
            CustomerId = filters.CustomerId,
            SubscriptionId = filters.SubscriptionId,
            StartDate = filters.StartDate,
            EndDate = filters.EndDate,
            SortOrder = sortOrder,
            Limit = limit,
            Offset = offset
        };
    }

    private static NpgsqlParameter CloneParameter(NpgsqlParameter source) =>
        new(source.ParameterName, source.Value ?? DBNull.Value);

    private static PaymentDto MapRow(NpgsqlDataReader reader)
    {
        return new PaymentDto
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            CreatedAt = ReadTimestamp(reader, "created_at").ToUniversalTime().ToString("O"),
            AmountPaid = reader.GetInt32(reader.GetOrdinal("amount_paid")),
            Currency = reader.GetString(reader.GetOrdinal("currency")),
            CustomerId = ReadNullableLong(reader, "customer_id"),
            SubscriptionId = ReadNullableLong(reader, "subscription_id"),
            BillingCycleId = ReadNullableLong(reader, "billing_cycle_id"),
            ExternalProductId = ReadNullableString(reader, "external_product_id"),
            DurationValue = ReadNullableInt(reader, "duration_value"),
            DurationUnit = ReadNullableString(reader, "duration_unit"),
            StripeInvoiceId = reader.GetString(reader.GetOrdinal("stripe_invoice_id")),
            StripeEventId = ReadNullableString(reader, "stripe_event_id"),
        };
    }

    private static DateTime ReadTimestamp(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.GetFieldValue<DateTime>(ordinal);
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long? ReadNullableLong(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static int? ReadNullableInt(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}
