using FluentAssertions;
using Stripe;
using Subscrio.Core.Application.DTOs;
using Subscrio.Payments.DTOs;
using Subscrio.Payments.Schema;
using Subscrio.Payments.Tests.Setup;
using Xunit;

namespace Subscrio.Payments.Tests.E2E;

public class PaymentsTests : IAsyncLifetime
{
    private PaymentsTestContext _ctx = null!;
    private PaymentTracker _payments = null!;

    public async Task InitializeAsync()
    {
        _ctx = await TestDatabase.SetupAsync();
        _payments = _ctx.Subscrio.UsePayments(new PaymentTrackerOptions
        {
            ConnectionString = _ctx.ConnectionString
        });
    }

    public async Task DisposeAsync()
    {
        if (_payments != null)
            await _payments.DisposeAsync();
        if (_ctx != null)
            await TestDatabase.TeardownAsync(_ctx.DbName, _ctx.Subscrio);
    }

    private static string Unique(string prefix) =>
        $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{Guid.NewGuid().ToString("N")[..6]}";

    private async Task<(string PriceId, string StripeCustomerId, string StripeSubId)> CreatePaidInvoiceFixtureAsync()
    {
        var product = await _ctx.Subscrio.Products.CreateProductAsync(new CreateProductDto(
            Key: Unique("prod"),
            DisplayName: "Payments Product"));
        var plan = await _ctx.Subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
            ProductKey: product.Key,
            Key: Unique("plan"),
            DisplayName: "Payments Plan"));
        var priceId = Unique("price");
        await _ctx.Subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
            PlanKey: plan.Key,
            Key: Unique("cycle"),
            DisplayName: "Monthly",
            DurationUnit: "months",
            DurationValue: 1,
            ExternalProductId: priceId));
        var stripeCustomerId = Unique("cus");
        var customer = await _ctx.Subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: Unique("cust"),
            DisplayName: "Payments Customer",
            ExternalBillingId: stripeCustomerId));

        var periodStart = DateTime.UtcNow;
        var periodEnd = periodStart.AddMonths(1);
        var stripeSubId = Unique("sub");
        var stripeSub = new Subscription
        {
            Id = stripeSubId,
            CustomerId = stripeCustomerId,
            Status = "active",
            Created = periodStart,
            CancelAtPeriodEnd = false,
            Metadata = new Dictionary<string, string>
            {
                ["subscrioCustomerKey"] = customer.Key
            },
            Items = new StripeList<SubscriptionItem>
            {
                Data =
                [
                    new SubscriptionItem
                    {
                        Id = Unique("si"),
                        Price = new Price { Id = priceId },
                        CurrentPeriodStart = periodStart,
                        CurrentPeriodEnd = periodEnd
                    }
                ]
            }
        };

        await _ctx.Subscrio.Stripe.ProcessStripeEventAsync(new Event
        {
            Id = Unique("evt"),
            Type = EventTypes.CustomerSubscriptionCreated,
            Data = new EventData { Object = stripeSub }
        });

        return (priceId, stripeCustomerId, stripeSubId);
    }

    private static Invoice BuildInvoice(
        string invoiceId,
        string stripeCustomerId,
        string stripeSubId,
        string priceId,
        long amountPaid)
    {
        var periodStart = DateTime.UtcNow;
        return new Invoice
        {
            Id = invoiceId,
            Object = "invoice",
            Status = "paid",
            AmountPaid = amountPaid,
            Currency = "usd",
            CustomerId = stripeCustomerId,
            Parent = new InvoiceParent
            {
                Type = "subscription_details",
                SubscriptionDetails = new InvoiceParentSubscriptionDetails
                {
                    SubscriptionId = stripeSubId
                }
            },
            Lines = new StripeList<InvoiceLineItem>
            {
                Data =
                [
                    new InvoiceLineItem
                    {
                        Id = Unique("il"),
                        Pricing = new InvoiceLineItemPricing
                        {
                            Type = "price_details",
                            PriceDetails = new InvoiceLineItemPricingPriceDetails
                            {
                                PriceId = priceId
                            }
                        },
                        Period = new InvoiceLineItemPeriod
                        {
                            Start = periodStart,
                            End = periodStart.AddMonths(1)
                        }
                    }
                ]
            }
        };
    }

    [Fact]
    public async Task InstallSchema_VerifySchema_IdempotentInstall()
    {
        (await _payments.VerifySchemaAsync()).Should().BeNull();

        await _payments.InstallSchemaAsync();
        (await _payments.VerifySchemaAsync()).Should().Be(Migrations.PaymentsSchemaVersion);

        await _payments.InstallSchemaAsync();
        (await _payments.VerifySchemaAsync()).Should().Be(Migrations.PaymentsSchemaVersion);

        var migrated = await _payments.MigrateAsync();
        migrated.Should().Be(0);
    }

    [Fact]
    public async Task InvoicePaymentSucceeded_WritesPaymentRow()
    {
        await _payments.InstallSchemaAsync();
        var (priceId, stripeCustomerId, stripeSubId) = await CreatePaidInvoiceFixtureAsync();

        var invoiceId = Unique("in");
        var eventId = Unique("evt");
        await _ctx.Subscrio.Stripe.ProcessStripeEventAsync(new Event
        {
            Id = eventId,
            Type = EventTypes.InvoicePaymentSucceeded,
            Data = new EventData
            {
                Object = BuildInvoice(invoiceId, stripeCustomerId, stripeSubId, priceId, 1999)
            }
        });

        var page = await _payments.ListAsync(new PaymentFilters { Limit = 500 });
        var row = page.Data.FirstOrDefault(r => r.StripeInvoiceId == invoiceId);
        row.Should().NotBeNull();
        row!.AmountPaid.Should().Be(1999);
        row.Currency.Should().Be("usd");
        row.StripeEventId.Should().Be(eventId);
        row.CustomerId.Should().NotBeNull();
        row.SubscriptionId.Should().NotBeNull();
        row.BillingCycleId.Should().NotBeNull();
        row.ExternalProductId.Should().Be(priceId);
        row.DurationValue.Should().Be(1);
        row.DurationUnit.Should().Be("months");

        var fetched = await _payments.GetAsync(row.Id);
        fetched!.Id.Should().Be(row.Id);
        fetched.AmountPaid.Should().Be(1999);

        var byCustomer = await _payments.ListAsync(new PaymentFilters { CustomerId = row.CustomerId });
        byCustomer.Data.Should().Contain(r => r.Id == row.Id);

        var bySubscription = await _payments.ListAsync(new PaymentFilters { SubscriptionId = row.SubscriptionId });
        bySubscription.Data.Should().Contain(r => r.Id == row.Id);
    }

    [Fact]
    public async Task DuplicateInvoicePaymentSucceeded_IsNoOp()
    {
        await _payments.InstallSchemaAsync();
        var (priceId, stripeCustomerId, stripeSubId) = await CreatePaidInvoiceFixtureAsync();

        var invoiceId = Unique("in");
        var invoice = BuildInvoice(invoiceId, stripeCustomerId, stripeSubId, priceId, 5000);

        await _ctx.Subscrio.Stripe.ProcessStripeEventAsync(new Event
        {
            Id = Unique("evt"),
            Type = EventTypes.InvoicePaymentSucceeded,
            Data = new EventData { Object = invoice }
        });
        await _ctx.Subscrio.Stripe.ProcessStripeEventAsync(new Event
        {
            Id = Unique("evt"),
            Type = EventTypes.InvoicePaymentSucceeded,
            Data = new EventData { Object = invoice }
        });

        var page = await _payments.ListAsync(new PaymentFilters { Limit = 500 });
        page.Data.Where(r => r.StripeInvoiceId == invoiceId).Should().HaveCount(1);
        page.Data.First(r => r.StripeInvoiceId == invoiceId).AmountPaid.Should().Be(5000);
    }

    [Fact]
    public async Task OtherStripeEvents_DoNotWritePaymentRows()
    {
        await _payments.InstallSchemaAsync();
        var (priceId, stripeCustomerId, stripeSubId) = await CreatePaidInvoiceFixtureAsync();
        var before = await _payments.ListAsync(new PaymentFilters { Limit = 500 });

        await _ctx.Subscrio.Stripe.ProcessStripeEventAsync(new Event
        {
            Id = Unique("evt"),
            Type = "invoice.payment_failed",
            Data = new EventData
            {
                Object = BuildInvoice(Unique("in"), stripeCustomerId, stripeSubId, priceId, 0)
            }
        });

        var periodStart = DateTime.UtcNow;
        await _ctx.Subscrio.Stripe.ProcessStripeEventAsync(new Event
        {
            Id = Unique("evt"),
            Type = EventTypes.CustomerSubscriptionUpdated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    Id = stripeSubId,
                    CustomerId = stripeCustomerId,
                    Status = "active",
                    Created = periodStart,
                    CancelAtPeriodEnd = false,
                    Metadata = new Dictionary<string, string>(),
                    Items = new StripeList<SubscriptionItem>
                    {
                        Data =
                        [
                            new SubscriptionItem
                            {
                                Id = Unique("si"),
                                Price = new Price { Id = priceId },
                                CurrentPeriodStart = periodStart,
                                CurrentPeriodEnd = periodStart.AddMonths(1)
                            }
                        ]
                    }
                }
            }
        });

        var after = await _payments.ListAsync(new PaymentFilters { Limit = 500 });
        after.Total.Should().Be(before.Total);
    }
}
