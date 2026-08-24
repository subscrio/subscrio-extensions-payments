import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import type { Subscrio } from 'subscrio';
import {
  createPaymentTracker,
  PAYMENTS_SCHEMA_VERSION,
  type PaymentTracker,
} from '../../src/index.js';
import { setupTestDatabase, teardownTestDatabase } from '../setup/database.js';

function unique(prefix: string): string {
  return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function buildStripeEvent(type: string, payload: Record<string, unknown>, eventId?: string) {
  return {
    id: eventId ?? unique('evt'),
    object: 'event',
    api_version: '2026-07-29.dahlia',
    created: Math.floor(Date.now() / 1000),
    data: { object: payload },
    livemode: false,
    pending_webhooks: 1,
    request: { id: null, idempotency_key: null },
    type,
  };
}

describe('Payments E2E', () => {
  let subscrio: Subscrio;
  let payments: PaymentTracker;
  let dbName: string;
  let connectionString: string;

  beforeAll(async () => {
    const ctx = await setupTestDatabase();
    subscrio = ctx.subscrio;
    dbName = ctx.dbName;
    connectionString = ctx.connectionString;
    payments = createPaymentTracker(subscrio, { database: { connectionString } });
  });

  afterAll(async () => {
    if (payments) await payments.dispose();
    await teardownTestDatabase(dbName, subscrio);
  });

  async function createPaidInvoiceFixture() {
    const product = await subscrio.products.createProduct({
      key: unique('prod'),
      displayName: 'Payments Product',
    });
    const plan = await subscrio.plans.createPlan({
      productKey: product.key,
      key: unique('plan'),
      displayName: 'Payments Plan',
    });
    const priceId = unique('price');
    await subscrio.billingCycles.createBillingCycle({
      planKey: plan.key,
      key: unique('cycle'),
      displayName: 'Monthly',
      durationValue: 1,
      durationUnit: 'months',
      externalProductId: priceId,
    });
    const stripeCustomerId = unique('cus');
    const customer = await subscrio.customers.createCustomer({
      key: unique('cust'),
      displayName: 'Payments Customer',
      externalBillingId: stripeCustomerId,
    });

    const stripeSubId = unique('sub');
    const subscriptionKey = unique('subkey');
    await subscrio.stripe.processStripeEvent(
      buildStripeEvent('customer.subscription.created', {
        id: stripeSubId,
        object: 'subscription',
        customer: stripeCustomerId,
        status: 'active',
        created: Math.floor(Date.now() / 1000),
        cancel_at_period_end: false,
        items: {
          data: [{
            price: { id: priceId },
            current_period_start: Math.floor(Date.now() / 1000),
            current_period_end: Math.floor(Date.now() / 1000) + 86400 * 30,
          }],
        },
        metadata: {
          subscrioCustomerKey: customer.key,
          subscrioSubscriptionKey: subscriptionKey,
        },
      }) as any
    );

    return { priceId, stripeCustomerId, stripeSubId };
  }

  test('installSchema / verifySchema / idempotent install', async () => {
    expect(await payments.verifySchema()).toBeNull();

    await payments.installSchema();
    expect(await payments.verifySchema()).toBe(PAYMENTS_SCHEMA_VERSION);

    await payments.installSchema();
    expect(await payments.verifySchema()).toBe(PAYMENTS_SCHEMA_VERSION);

    const migrated = await payments.migrate();
    expect(migrated).toBe(0);
  });

  test('invoice.payment_succeeded writes a payment row', async () => {
    await payments.installSchema();
    const { priceId, stripeCustomerId, stripeSubId } = await createPaidInvoiceFixture();

    const invoiceId = unique('in');
    const eventId = unique('evt');
    const periodStart = Math.floor(Date.now() / 1000);
    const periodEnd = periodStart + 2_592_000;

    await subscrio.stripe.processStripeEvent(
      buildStripeEvent(
        'invoice.payment_succeeded',
        {
          id: invoiceId,
          object: 'invoice',
          status: 'paid',
          amount_paid: 1999,
          currency: 'usd',
          customer: stripeCustomerId,
          subscription: stripeSubId,
          parent: {
            type: 'subscription_details',
            subscription_details: { subscription: stripeSubId },
          },
          lines: {
            object: 'list',
            data: [
              {
                id: unique('il'),
                object: 'line_item',
                type: 'subscription',
                pricing: {
                  type: 'price_details',
                  price_details: { price: priceId },
                },
                price: { id: priceId, object: 'price' },
              pricing: {
                type: 'price_details',
                price_details: { price: priceId },
              },
                period: { start: periodStart, end: periodEnd },
              },
            ],
          },
        },
        eventId
      ) as any
    );

    const { data } = await payments.list({ limit: 500 });
    const row = data.find((r) => r.stripeInvoiceId === invoiceId);
    expect(row).toBeDefined();
    expect(row!.amountPaid).toBe(1999);
    expect(row!.currency).toBe('usd');
    expect(row!.stripeEventId).toBe(eventId);
    expect(row!.customerId).toBeTypeOf('number');
    expect(row!.subscriptionId).toBeTypeOf('number');
    expect(row!.billingCycleId).toBeTypeOf('number');
    expect(row!.externalProductId).toBe(priceId);
    expect(row!.durationValue).toBe(1);
    expect(row!.durationUnit).toBe('months');

    const fetched = await payments.get(row!.id);
    expect(fetched?.id).toBe(row!.id);
    expect(fetched?.amountPaid).toBe(1999);

    const byCustomer = await payments.list({ customerId: row!.customerId! });
    expect(byCustomer.data.some((r) => r.id === row!.id)).toBe(true);

    const bySubscription = await payments.list({ subscriptionId: row!.subscriptionId! });
    expect(bySubscription.data.some((r) => r.id === row!.id)).toBe(true);
  });

  test('duplicate invoice.payment_succeeded is a no-op', async () => {
    await payments.installSchema();
    const { priceId, stripeCustomerId, stripeSubId } = await createPaidInvoiceFixture();

    const invoiceId = unique('in');
    const payload = {
      id: invoiceId,
      object: 'invoice',
      status: 'paid',
      amount_paid: 5000,
      currency: 'usd',
      customer: stripeCustomerId,
      subscription: stripeSubId,
      parent: {
        type: 'subscription_details',
        subscription_details: { subscription: stripeSubId },
      },
      lines: {
        object: 'list',
        data: [
          {
            id: unique('il'),
            object: 'line_item',
            type: 'subscription',
            price: { id: priceId, object: 'price' },
            period: {
              start: Math.floor(Date.now() / 1000),
              end: Math.floor(Date.now() / 1000) + 2_592_000,
            },
          },
        ],
      },
    };

    await subscrio.stripe.processStripeEvent(
      buildStripeEvent('invoice.payment_succeeded', payload, unique('evt')) as any
    );
    await subscrio.stripe.processStripeEvent(
      buildStripeEvent('invoice.payment_succeeded', payload, unique('evt')) as any
    );

    const { data } = await payments.list({ limit: 500 });
    const matches = data.filter((r) => r.stripeInvoiceId === invoiceId);
    expect(matches).toHaveLength(1);
    expect(matches[0].amountPaid).toBe(5000);
  });

  test('other Stripe events do not write payment rows', async () => {
    await payments.installSchema();
    const { priceId, stripeCustomerId, stripeSubId } = await createPaidInvoiceFixture();
    const before = await payments.list({ limit: 500 });

    await subscrio.stripe.processStripeEvent(
      buildStripeEvent('invoice.payment_failed', {
        id: unique('in'),
        object: 'invoice',
        status: 'open',
        amount_paid: 0,
        currency: 'usd',
        customer: stripeCustomerId,
        subscription: stripeSubId,
        lines: {
          object: 'list',
          data: [
            {
              id: unique('il'),
              object: 'line_item',
              price: { id: priceId, object: 'price' },
              pricing: {
                type: 'price_details',
                price_details: { price: priceId },
              },
            },
          ],
        },
      }) as any
    );

    await subscrio.stripe.processStripeEvent(
      buildStripeEvent('customer.subscription.updated', {
        id: stripeSubId,
        object: 'subscription',
        customer: stripeCustomerId,
        status: 'active',
        created: Math.floor(Date.now() / 1000),
        cancel_at_period_end: false,
        items: {
          data: [{
            price: { id: priceId },
            current_period_start: Math.floor(Date.now() / 1000),
            current_period_end: Math.floor(Date.now() / 1000) + 86400 * 30,
          }],
        },
        metadata: {},
      }) as any
    );

    const after = await payments.list({ limit: 500 });
    expect(after.total).toBe(before.total);
  });
});
