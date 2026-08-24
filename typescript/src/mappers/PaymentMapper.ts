import type { PaymentDto } from '../dtos/PaymentDto.js';

/** Raw row from subscrio.payments */
export interface PaymentRecord {
  id: string | number;
  created_at: Date | string;
  amount_paid: string | number;
  currency: string;
  customer_id: string | number | null;
  subscription_id: string | number | null;
  billing_cycle_id: string | number | null;
  external_product_id: string | null;
  duration_value: string | number | null;
  duration_unit: string | null;
  stripe_invoice_id: string;
  stripe_event_id: string | null;
}

function toNumber(value: string | number | null | undefined): number | null {
  if (value === null || value === undefined) return null;
  return typeof value === 'number' ? value : Number(value);
}

function toIso(value: Date | string): string {
  if (value instanceof Date) return value.toISOString();
  return new Date(value).toISOString();
}

export class PaymentMapper {
  static toDto(record: PaymentRecord): PaymentDto {
    return {
      id: toNumber(record.id)!,
      createdAt: toIso(record.created_at),
      amountPaid: toNumber(record.amount_paid) ?? 0,
      currency: record.currency,
      customerId: toNumber(record.customer_id),
      subscriptionId: toNumber(record.subscription_id),
      billingCycleId: toNumber(record.billing_cycle_id),
      externalProductId: record.external_product_id,
      durationValue: toNumber(record.duration_value),
      durationUnit: record.duration_unit,
      stripeInvoiceId: record.stripe_invoice_id,
      stripeEventId: record.stripe_event_id,
    };
  }
}
