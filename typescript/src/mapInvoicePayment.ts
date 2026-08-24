import type { StripeReceivedHookEvent } from 'subscrio';

export interface ExtractedInvoicePayment {
  amountPaid: number;
  currency: string;
  stripeInvoiceId: string;
  stripeEventId: string | null;
  stripeSubscriptionId: string;
}

function asId(value: unknown): string | undefined {
  if (typeof value === 'string' && value.length > 0) return value;
  if (value && typeof value === 'object' && 'id' in value) {
    const id = (value as { id?: unknown }).id;
    if (typeof id === 'string' && id.length > 0) return id;
  }
  return undefined;
}

function asAmount(value: unknown): number {
  if (typeof value === 'number' && Number.isFinite(value)) return Math.trunc(value);
  if (typeof value === 'string' && value.length > 0) {
    const n = Number(value);
    if (Number.isFinite(n)) return Math.trunc(n);
  }
  return 0;
}

/**
 * Pull payment fields from a stripe.received.after hook when the event is invoice.payment_succeeded.
 * Returns null for other event types or invoices without a Stripe subscription / invoice id.
 */
export function mapInvoicePayment(event: StripeReceivedHookEvent): ExtractedInvoicePayment | null {
  if (event.data?.type !== 'invoice.payment_succeeded') {
    return null;
  }

  const obj = event.data.data?.object as unknown as Record<string, unknown> | undefined;
  if (!obj) {
    return null;
  }

  const stripeInvoiceId = asId(obj.id);
  if (!stripeInvoiceId) {
    return null;
  }

  const parent = obj.parent as Record<string, unknown> | undefined;
  const subscriptionDetails = parent?.subscription_details as Record<string, unknown> | undefined;
  const stripeSubscriptionId =
    asId(obj.subscription) ?? asId(subscriptionDetails?.subscription);
  if (!stripeSubscriptionId) {
    return null;
  }

  return {
    amountPaid: asAmount(obj.amount_paid),
    currency: typeof obj.currency === 'string' ? obj.currency : '',
    stripeInvoiceId,
    stripeEventId: typeof event.data.id === 'string' ? event.data.id : null,
    stripeSubscriptionId,
  };
}
