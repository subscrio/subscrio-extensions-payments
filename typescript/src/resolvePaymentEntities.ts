import type { Pool } from 'pg';

export interface PaymentEntityAssociation {
  customerId: number;
  subscriptionId: number;
  billingCycleId: number | null;
  externalProductId: string | null;
  durationValue: number | null;
  durationUnit: string | null;
}

/**
 * Resolve Subscrio subscription, customer, and billing cycle from a Stripe subscription ID.
 */
export async function resolvePaymentEntities(
  pool: Pool,
  stripeSubscriptionId: string
): Promise<PaymentEntityAssociation | null> {
  const { rows } = await pool.query<{
    subscription_id: string;
    customer_id: string;
    billing_cycle_id: string | null;
    external_product_id: string | null;
    duration_value: string | number | null;
    duration_unit: string | null;
  }>(
    `SELECT
       s.id AS subscription_id,
       s.customer_id,
       s.billing_cycle_id,
       bc.external_product_id,
       bc.duration_value,
       bc.duration_unit
     FROM subscrio.subscriptions s
     LEFT JOIN subscrio.billing_cycles bc ON bc.id = s.billing_cycle_id
     WHERE s.stripe_subscription_id = $1
     LIMIT 1`,
    [stripeSubscriptionId]
  );

  const row = rows[0];
  if (!row) {
    return null;
  }

  return {
    subscriptionId: Number(row.subscription_id),
    customerId: Number(row.customer_id),
    billingCycleId: row.billing_cycle_id != null ? Number(row.billing_cycle_id) : null,
    externalProductId: row.external_product_id,
    durationValue: row.duration_value != null ? Number(row.duration_value) : null,
    durationUnit: row.duration_unit,
  };
}
