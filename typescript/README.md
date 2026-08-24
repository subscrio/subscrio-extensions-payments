# subscrio-payments

First-party payments extension for [subscrio](https://github.com/subscrio/subscrio-typescript). Records Stripe `invoice.payment_succeeded` amounts in Postgres via `stripe.received.after`.

## Install

```bash
npm install subscrio-payments
```

Peer dependency: `subscrio`.

## Usage

```typescript
import { Subscrio } from 'subscrio';
import { createPaymentTracker } from 'subscrio-payments';

const connectionString = process.env.DATABASE_URL!;
const subscrio = new Subscrio({ database: { connectionString } });
await subscrio.installSchema();

const payments = createPaymentTracker(subscrio, { database: { connectionString } });
await payments.installSchema();

const { data, total } = await payments.list({ customerId: 1, limit: 50 });
const row = await payments.get(data[0]?.id);

await payments.dispose(); // unsubscribes the hook and closes the pool
```

## Schema

Owned by this extension (not core). Created only when you call `installSchema()`.

- Table: `subscrio.payments`
- Version key in `subscrio.system_config`: `payments_schema_version` (current: `1.0.0`)
- `verifySchema()` → version string or `null`
- `migrate()` applies future extension-only migrations

Nullable FKs (`customer_id`, `subscription_id`, `billing_cycle_id`) use `ON DELETE SET NULL`. Billing cycle interval is stored as `duration_value` + `duration_unit`. Amounts are Stripe integer cents plus `currency`. Duplicate webhooks are ignored (`UNIQUE` on `stripe_invoice_id`).

## Hooks

Registers `stripe.received.after` only. Other Stripe event types are ignored. Invoices without a Stripe subscription id, or whose Subscrio subscription cannot be resolved, are skipped.

## After-hook failure semantics

Payment writes run on the **after** hook, so Subscrio’s invoice handling is already committed when the insert runs.

- If the payment insert throws, the error **propagates** and `processStripeEvent` fails.
- The underlying subscription period update **remains** in the database.
- Call `dispose()` to unsubscribe; further Stripe events will not write payment rows.

## Query

```typescript
await payments.list({
  customerId: 1,
  subscriptionId: 2,
  startDate: '2026-01-01T00:00:00.000Z',
  endDate: '2026-12-31T23:59:59.999Z',
  sortOrder: 'desc',
  limit: 50,
  offset: 0,
});
// → { data: PaymentDto[], total: number }
```

## Development

From this directory (`typescript/`):

```bash
npm install
npm run build
npm test
```

`npm install` resolves the `subscrio` peer from the hub checkout at `core/typescript`. Check out the [hub workspace layout](https://github.com/subscrio/subscrio/blob/main/repos.md) first. Core tests in [subscrio-typescript](https://github.com/subscrio/subscrio-typescript) do not run this suite.

Tests create a fresh Postgres database. Set `TEST_DATABASE_URL`, add a `typescript/.env`, or use default localhost credentials (`postgresql://postgres:postgres@localhost:5432/postgres`). In the hub workspace, `core/typescript/.env` is also loaded when those are unset.
