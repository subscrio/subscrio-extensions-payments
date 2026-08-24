import { Pool } from 'pg';
import type { Subscrio } from 'subscrio';
import { HookEvents, type StripeReceivedHookEvent } from 'subscrio';
import type { PaymentDto, PaymentFilterDto, PaymentListResult } from './dtos/PaymentDto.js';
import { mapInvoicePayment } from './mapInvoicePayment.js';
import { PostgresPaymentRepository } from './repository/PostgresPaymentRepository.js';
import { resolvePaymentEntities } from './resolvePaymentEntities.js';
import { PaymentsSchemaInstaller } from './schema/installer.js';

export interface PaymentTrackerDatabaseConfig {
  connectionString: string;
}

export interface PaymentTrackerOptions {
  database: PaymentTrackerDatabaseConfig;
}

export interface PaymentTracker {
  installSchema(): Promise<void>;
  verifySchema(): Promise<string | null>;
  migrate(): Promise<number>;
  list(filters?: Partial<PaymentFilterDto>): Promise<PaymentListResult>;
  get(id: number): Promise<PaymentDto | null>;
  dispose(): Promise<void>;
}

/**
 * Create a payments extension bound to a Subscrio instance.
 *
 * Registers a handler for `stripe.received.after`. Rows are written after Stripe
 * handling has already committed, so:
 * - If the payment insert throws, processStripeEvent still fails (hook error propagates),
 *   but the underlying subscription period update remains in the database.
 * - Call `dispose()` to unsubscribe the handler and close the connection pool.
 */
export function createPaymentTracker(
  subscrio: Subscrio,
  options: PaymentTrackerOptions
): PaymentTracker {
  const pool = new Pool({ connectionString: options.database.connectionString });
  const installer = new PaymentsSchemaInstaller(pool);
  const repository = new PostgresPaymentRepository(pool);
  const unsubscribers: Array<() => void> = [];

  const writeStripe = async (event: StripeReceivedHookEvent) => {
    const extracted = mapInvoicePayment(event);
    if (!extracted) {
      return;
    }

    const association = await resolvePaymentEntities(pool, extracted.stripeSubscriptionId);
    if (!association) {
      return;
    }

    await repository.insert({
      amountPaid: extracted.amountPaid,
      currency: extracted.currency,
      customerId: association.customerId,
      subscriptionId: association.subscriptionId,
      billingCycleId: association.billingCycleId,
      externalProductId: association.externalProductId,
      durationValue: association.durationValue,
      durationUnit: association.durationUnit,
      stripeInvoiceId: extracted.stripeInvoiceId,
      stripeEventId: extracted.stripeEventId,
    });
  };

  unsubscribers.push(subscrio.hooks.on(HookEvents.StripeReceivedAfter, writeStripe));

  let disposed = false;

  return {
    async installSchema() {
      await installer.install();
    },
    async verifySchema() {
      return installer.verify();
    },
    async migrate() {
      return installer.migrate();
    },
    async list(filters) {
      return repository.list(filters);
    },
    async get(id) {
      return repository.get(id);
    },
    async dispose() {
      if (disposed) return;
      disposed = true;
      for (const off of unsubscribers) {
        off();
      }
      unsubscribers.length = 0;
      await pool.end();
    },
  };
}
