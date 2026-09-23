# Subscrio.Payments

First-party payments extension for [Subscrio.Core](https://github.com/subscrio/subscrio-dotnet). Records Stripe `invoice.payment_succeeded` amounts in Postgres via `stripe.received.after`.

## Install

```bash
dotnet add package Subscrio.Payments
```

Depends on `Subscrio.Core` and PostgreSQL (Npgsql).

## Usage

```csharp
using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Payments;
using Subscrio.Payments.DTOs;

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")!;
var subscrio = new Subscrio(new SubscrioConfig
{
    Database = new DatabaseConfig { ConnectionString = connectionString }
});
await subscrio.InstallSchemaAsync();

await using var payments = subscrio.UsePayments(new PaymentTrackerOptions
{
    ConnectionString = connectionString
});
await payments.InstallSchemaAsync();

var page = await payments.ListAsync(new PaymentFilters
{
    CustomerId = 1,
    Limit = 50
});
var row = page.Data.Count > 0 ? await payments.GetAsync(page.Data[0].Id) : null;

// Dispose unsubscribes the hook and closes the Npgsql data source
```

## Schema

Owned by this extension (not core). Created only when you call `InstallSchemaAsync()`.

- Table: `subscrio.payments`
- Version key in `subscrio.system_config`: `payments_schema_version` (current: `1.0.0`)
- `VerifySchemaAsync()` → version string or `null`
- `MigrateAsync()` applies future extension-only migrations

Nullable FKs (`customer_id`, `subscription_id`, `billing_cycle_id`) use `ON DELETE SET NULL`. Billing cycle interval is stored as `duration_value` + `duration_unit`. Amounts are Stripe integer cents plus `currency`. Duplicate webhooks are ignored (`UNIQUE` on `stripe_invoice_id`).

## Hooks

Registers `OnStripeReceivedAfter` only. Other Stripe event types are ignored. Invoices without a Stripe subscription id, or whose Subscrio subscription cannot be resolved, are skipped.

## After-hook failure semantics

Payment writes run on the **after** hook, so Subscrio’s invoice handling is already committed when the insert runs.

- If the payment insert throws, the error **propagates** and `ProcessStripeEventAsync` fails.
- The underlying subscription period update **remains** in the database.
- Call `DisposeAsync()` to unsubscribe; further Stripe events will not write payment rows.

## Query

```csharp
await payments.ListAsync(new PaymentFilters
{
    CustomerId = 1,
    SubscriptionId = 2,
    StartDate = DateTime.Parse("2026-01-01T00:00:00.000Z").ToUniversalTime(),
    EndDate = DateTime.Parse("2026-12-31T23:59:59.999Z").ToUniversalTime(),
    SortOrder = "desc",
    Limit = 50,
    Offset = 0,
});
// → PaymentPage { Data, Total }
```

## Development

From this directory (`dotnet/`):

```bash
dotnet build
dotnet test
```

Builds and tests use published `Subscrio.Core` 0.5.1 by default. In the [hub workspace](https://github.com/subscrio/subscrio/blob/main/repos.md), pass `-p:UseLocalSubscrioCore=true` to build against the local core checkout instead. Core tests in [subscrio-dotnet](https://github.com/subscrio/subscrio-dotnet) do not run this suite.

Tests create a fresh Postgres database. Set `TEST_DATABASE_URL`, copy `tests/appsettings.example.json` to `tests/appsettings.json`, or use default localhost credentials (`postgres` / `postgres`).
