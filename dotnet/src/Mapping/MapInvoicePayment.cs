using Stripe;
using Subscrio.Core.Application.Hooks;

namespace Subscrio.Payments.Mapping;

public sealed class ExtractedInvoicePayment
{
    public required int AmountPaid { get; init; }
    public required string Currency { get; init; }
    public required string StripeInvoiceId { get; init; }
    public string? StripeEventId { get; init; }
    public required string StripeSubscriptionId { get; init; }
}

public static class MapInvoicePayment
{
    public static ExtractedInvoicePayment? FromEvent(StripeReceivedHookEvent evt)
    {
        if (evt.Data?.Type != EventTypes.InvoicePaymentSucceeded)
            return null;

        if (evt.Data.Data?.Object is not Invoice invoice)
            return null;

        if (string.IsNullOrEmpty(invoice.Id))
            return null;

        var stripeSubscriptionId = AsId(invoice.Parent?.SubscriptionDetails?.SubscriptionId)
            ?? AsId(invoice.Parent?.SubscriptionDetails?.Subscription?.Id);
        if (string.IsNullOrEmpty(stripeSubscriptionId))
            return null;

        var amountPaid = invoice.AmountPaid > int.MaxValue
            ? int.MaxValue
            : (int)invoice.AmountPaid;

        return new ExtractedInvoicePayment
        {
            AmountPaid = amountPaid,
            Currency = invoice.Currency ?? string.Empty,
            StripeInvoiceId = invoice.Id,
            StripeEventId = string.IsNullOrEmpty(evt.Data.Id) ? null : evt.Data.Id,
            StripeSubscriptionId = stripeSubscriptionId
        };
    }

    private static string? AsId(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
