namespace Subscrio.Payments;

public static class PaymentTrackerExtensions
{
    /// <summary>
    /// Create a payments extension bound to a Subscrio instance.
    /// Registers a handler for stripe.received.after. Dispose to unsubscribe.
    /// </summary>
    public static PaymentTracker UsePayments(this Subscrio.Core.Subscrio subscrio, PaymentTrackerOptions options) =>
        new(subscrio, options);
}
