using Npgsql;
using Subscrio.Core.Application.Hooks;
using Subscrio.Payments.DTOs;
using Subscrio.Payments.Mapping;
using Subscrio.Payments.Repository;
using Subscrio.Payments.Schema;

namespace Subscrio.Payments;

/// <summary>
/// Payments extension bound to a Subscrio instance.
/// Registers a handler for stripe.received.after and records invoice.payment_succeeded rows.
/// </summary>
public sealed class PaymentTracker : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SchemaInstaller _installer;
    private readonly PostgresPaymentRepository _repository;
    private readonly List<Action> _unsubscribers = new();
    private bool _disposed;

    internal PaymentTracker(Subscrio.Core.Subscrio subscrio, PaymentTrackerOptions options)
    {
        ArgumentNullException.ThrowIfNull(subscrio);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("ConnectionString is required", nameof(options));

        _dataSource = NpgsqlDataSource.Create(options.ConnectionString);
        _installer = new SchemaInstaller(_dataSource);
        _repository = new PostgresPaymentRepository(_dataSource);

        async Task WriteStripe(StripeReceivedHookEvent evt, CancellationToken ct)
        {
            var extracted = MapInvoicePayment.FromEvent(evt);
            if (extracted == null)
                return;

            var association = await ResolvePaymentEntities.ResolveAsync(
                _dataSource,
                extracted.StripeSubscriptionId,
                ct);
            if (association == null)
                return;

            await _repository.InsertAsync(
                new InsertPaymentInput
                {
                    AmountPaid = extracted.AmountPaid,
                    Currency = extracted.Currency,
                    CustomerId = association.CustomerId,
                    SubscriptionId = association.SubscriptionId,
                    BillingCycleId = association.BillingCycleId,
                    ExternalProductId = association.ExternalProductId,
                    DurationValue = association.DurationValue,
                    DurationUnit = association.DurationUnit,
                    StripeInvoiceId = extracted.StripeInvoiceId,
                    StripeEventId = extracted.StripeEventId
                },
                ct);
        }

        _unsubscribers.Add(subscrio.Hooks.OnStripeReceivedAfter(WriteStripe));
    }

    public Task InstallSchemaAsync(CancellationToken cancellationToken = default) =>
        _installer.InstallAsync(cancellationToken);

    public Task<string?> VerifySchemaAsync(CancellationToken cancellationToken = default) =>
        _installer.VerifyAsync(cancellationToken);

    public Task<int> MigrateAsync(CancellationToken cancellationToken = default) =>
        _installer.MigrateAsync(cancellationToken);

    public Task<PaymentPage> ListAsync(
        PaymentFilters? filters = null,
        CancellationToken cancellationToken = default) =>
        _repository.ListAsync(filters, cancellationToken);

    public Task<PaymentDto?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        _repository.GetAsync(id, cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        foreach (var off in _unsubscribers)
            off();
        _unsubscribers.Clear();
        _dataSource.Dispose();
        return ValueTask.CompletedTask;
    }
}
