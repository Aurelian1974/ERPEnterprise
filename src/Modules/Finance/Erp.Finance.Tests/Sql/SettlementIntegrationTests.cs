using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Messaging;
using Erp.Finance.Application;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain.Settlement;
using Erp.Finance.Infrastructure;
using Erp.Finance.Infrastructure.Events;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Decontarile cu partenerii (02.02, F3): facturile din evenimente devin partide, plata se aloca pe facturile indicate si FIFO, TVA-ul la
/// incasare da exact TVA-ul facturii, desfacerea, regula SPV, concurenta si soldul la o data.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SettlementIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static long invoiceSequence = DateTime.UtcNow.Ticks % 1_000_000_000;

    public SettlementIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.PartnerBalanceView, FinancePermissions.AllocationManage, FinancePermissions.AllocationUndo,
            FinancePermissions.OpenItemSetDispute, FinancePermissions.OpenItemBlockPayment, FinancePermissions.PartnerProfileManage]);
    }

    private static readonly DateOnly Day = new(2026, 10, 1);

    private async Task<int> NewPartner(string country = "RO", byte type = 1)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Partener Decontări SRL', @Type, @FiscalCode, 1, 1, 1, N'Str. Mare 1', N'Cluj-Napoca', CASE WHEN @Country = 'RO' THEN N'CJ' END, @Country, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("PD-"), Type = type, Country = country,
            FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private static FacturaEmisa Sale(int partner, decimal total, DateOnly date, DateOnly due, decimal vat = 0, bool vatOnCollection = false,
        long? reversedId = null) =>
        new(Interlocked.Increment(ref invoiceSequence), partner, $"FV-{invoiceSequence}", date, date, "RON", 1m, total, total, vat, vat, vatOnCollection,
            reversedId is not null, reversedId, false, [new InvoiceDueDate(due, total)], null, null, EInvoiceStatus.Accepted, null);

    private static FacturaFurnizorValidata Purchase(int partner, decimal total, DateOnly date, EInvoiceStatus status = EInvoiceStatus.ReceivedFromSpv) =>
        new(Interlocked.Increment(ref invoiceSequence), partner, $"FF-{invoiceSequence}", date, date, "RON", 1m, total, total, 0, 0, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, status, null);

    private Task<long> Register(FacturaEmisa invoice) =>
        InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(invoice, Ct));

    private Task<long> Register(FacturaFurnizorValidata invoice) =>
        InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(invoice, Ct));

    private static long paymentSequence = DateTime.UtcNow.Ticks % 1_000_000_000;

    private Task<PaymentSettlementResult> Pay(int partner, PartnerRole role, decimal amount, DateOnly date, bool confirm = false, params SettlementTarget[] targets) =>
        InTransaction(sp => sp.GetRequiredService<ISettlementService>().RegisterPaymentAsync(new PaymentSettlementRequest(Database.CompanyId, partner, role,
            "Finance.CashDocument", Interlocked.Increment(ref paymentSequence), "", $"CH-{paymentSequence}", date, "RON", 1m, amount, amount, false, targets,
            confirm), Ct));

    private Task<(decimal Amount, decimal Open)> Item(long id) =>
        Query<(decimal, decimal)>("SELECT Amount, OpenAmount FROM fin.OpenItem WHERE Id = @Id", new { Id = id });

    [Fact]
    [Trait("Criteriu", "02.02-F3-A2")]
    public async Task An_invoice_event_delivered_twice_creates_one_open_item()
    {
        var partner = await NewPartner();
        var invoice = Sale(partner, 1000m, Day, Day.AddDays(30));
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            foreach (var evt in new[] { invoice, invoice with { EventId = Guid.NewGuid() } })
            {
                await connection.ExecuteAsync("""
                    INSERT INTO core.Outbox (EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, OccurredAt)
                    VALUES (@EventId, @EventType, 1, @Company, NEWID(), @Payload, SYSUTCDATETIME());
                    """, new { evt.EventId, EventType = typeof(FacturaEmisa).FullName, Company = Database.CompanyId,
                    Payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
            }
        }

        var services = new ServiceCollection().AddLogging().AddOutboxDispatcher(Database.ConnectionString);
        services.AddFinanceInfrastructure(Database.ConnectionString);
        services.AddIntegrationEventHandler<FacturaEmisa, RegisterInvoices>();
        var dispatcher = services.BuildServiceProvider().GetRequiredService<OutboxDispatcher>();
        await dispatcher.RunOnceAsync(Ct);
        await dispatcher.RunOnceAsync(Ct);

        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.OpenItem WHERE SourceDocumentType = 'Sales.Invoice' AND SourceDocumentId = @Id",
            new { Id = invoice.InvoiceId }));
        Assert.Equal((Database.CompanyId, 1000m, "4111"), await Query<(int, decimal, string)>(
            "SELECT CompanyId, OpenAmount, GlAccountCode FROM fin.OpenItem WHERE SourceDocumentType = 'Sales.Invoice' AND SourceDocumentId = @Id",
            new { Id = invoice.InvoiceId }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A3")]
    public async Task A_receipt_settles_the_indicated_invoice_and_the_surplus_goes_fifo_or_stays_unallocated()
    {
        var partner = await NewPartner();
        var older = await Register(Sale(partner, 500m, Day, Day.AddDays(10)));
        var newer = await Register(Sale(partner, 1000m, Day.AddDays(5), Day.AddDays(40)));
        var payment = await Pay(partner, PartnerRole.Customer, 1200m, Day.AddDays(20), false, new SettlementTarget(newer, null));

        Assert.Equal((1200m, 0m), (payment.AllocatedAmount, payment.UnallocatedAmount));
        Assert.Equal((1000m, 0m), await Item(newer));
        Assert.Equal((500m, 300m), await Item(older));
        Assert.Contains(newer, payment.ClosedOpenItemIds);
        Assert.Equal((-1200m, 0m), await Item(payment.PaymentOpenItemId));

        // Mod 1 (doar factura indicata): surplusul ramane plata nealocata; o factura noua nu o mai consuma automat.
        var strict = await NewPartner();
        await Send(new SavePartnerFinanceProfile(strict, PartnerRole.Customer, AutoAllocationMode.IndicatedOnly, "0", 0, false, false, false,
            null, null, null));
        var invoice = await Register(Sale(strict, 100m, Day, Day.AddDays(10)));
        var paid = await Pay(strict, PartnerRole.Customer, 150m, Day.AddDays(1), false, new SettlementTarget(invoice, null));
        Assert.Equal((100m, 50m), (paid.AllocatedAmount, paid.UnallocatedAmount));
        Assert.Equal((-150m, -50m), await Item(paid.PaymentOpenItemId));

        // Avansul incasat inaintea facturii (mod 2): factura noua il consuma automat.
        var advance = await Pay(partner, PartnerRole.Customer, 700m, Day.AddDays(21));
        Assert.Equal(300m, advance.AllocatedAmount); // restul facturii vechi, FIFO
        var later = await Register(Sale(partner, 250m, Day.AddDays(25), Day.AddDays(55)));
        Assert.Equal((250m, 0m), await Item(later));
        Assert.Equal((-700m, -150m), await Item(advance.PaymentOpenItemId));

        var balance = Assert.Single(await Send(new GetPartnerBalance(partner, PartnerRole.Customer, Day.AddDays(30))));
        Assert.Equal((-150m, -150m), (balance.Balance, balance.UnallocatedPaymentsRon));
        Assert.Empty(await Send(new GetOpenItems(partner, PartnerRole.Supplier)));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A5")]
    public async Task The_open_amount_never_changes_sign_or_exceeds_the_item()
    {
        var partner = await NewPartner();
        var invoice = await Register(Sale(partner, 100m, Day, Day.AddDays(10)));
        var payment = await Pay(partner, PartnerRole.Customer, 300m, Day.AddDays(1), false, new SettlementTarget(invoice, null));
        var second = await Register(Sale(partner, 400m, Day.AddDays(2), Day.AddDays(12)));
        Assert.Equal((400m, 200m), await Item(second)); // restul de 200 al platii s-a dus FIFO pe factura noua

        var exceeds = await Assert.ThrowsAsync<ErpException>(() => Send(new AllocateManually(partner, PartnerRole.Customer,
            [new AllocationPairInput(second, payment.PaymentOpenItemId, "250")])));
        Assert.Equal("FIN_ALLOCATION_EXCEEDS_OPEN", exceeds.Code);
        var sides = await Assert.ThrowsAsync<ErpException>(() => Send(new AllocateManually(partner, PartnerRole.Customer,
            [new AllocationPairInput(payment.PaymentOpenItemId, second, "10")])));
        Assert.Equal("FIN_ALLOCATION_SIDES_INVALID", sides.Code);

        await using var connection = new SqlConnection(Database.ConnectionString);
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("UPDATE fin.OpenItem SET OpenAmount = -10 WHERE Id = @Id", new { Id = second }));
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("UPDATE fin.OpenItem SET OpenAmount = 500 WHERE Id = @Id", new { Id = second }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A7")]
    [Trait("Criteriu", "02.02-F3-A6")]
    public async Task Vat_on_collection_paid_in_three_tranches_sums_to_the_invoice_vat_and_undo_reverses_it()
    {
        var partner = await NewPartner();
        var invoice = await Register(Sale(partner, 1190m, Day, Day.AddDays(30), vat: 190m, vatOnCollection: true));
        await Pay(partner, PartnerRole.Customer, 400m, Day.AddDays(1), false, new SettlementTarget(invoice, null));
        await Pay(partner, PartnerRole.Customer, 400m, Day.AddDays(2), false, new SettlementTarget(invoice, null));
        await Pay(partner, PartnerRole.Customer, 390m, Day.AddDays(3), false, new SettlementTarget(invoice, null));

        var entries = (await QueryAll<(decimal Base, decimal Vat)>(
            "SELECT BaseAmountRon, VatAmountRon FROM fin.VatCashEntry WHERE OpenItemId = @Id ORDER BY Id", new { Id = invoice })).ToList();
        Assert.Equal([63.87m, 63.87m, 62.26m], entries.Select(e => e.Vat));
        Assert.Equal((1000m, 190m), (entries.Sum(e => e.Base), entries.Sum(e => e.Vat)));
        Assert.Equal((byte)1, await Query<byte>("SELECT TOP 1 Side FROM fin.VatCashEntry WHERE OpenItemId = @Id", new { Id = invoice }));

        // Desfacerea ultimei alocari: restul revine, TVA-ul se inverseaza; intr-o perioada inchisa e refuzata.
        var last = await Query<long>("SELECT MAX(Id) FROM fin.Allocation WHERE DebitOpenItemId = @Id", new { Id = invoice });
        ClosedDays.Add(Day.AddDays(3));
        var closed = await Assert.ThrowsAsync<ErpException>(() => Send(new UndoAllocation(last, "Încasare greșită")));
        Assert.Equal(CoreErrorCodes.PeriodClosed, closed.Code);
        ClosedDays.Clear();

        await Send(new UndoAllocation(last, "Încasare greșită"));
        Assert.Equal((1190m, 390m), await Item(invoice));
        Assert.Equal(127.74m, await Query<decimal>("SELECT SUM(VatAmountRon) FROM fin.VatCashEntry WHERE OpenItemId = @Id", new { Id = invoice }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.VatCashEntry WHERE OpenItemId = @Id AND IsReversal = 1", new { Id = invoice }));
        var again = await Assert.ThrowsAsync<ErpException>(() => Send(new UndoAllocation(last, "a doua oară")));
        Assert.Equal("FIN_ALLOCATION_UNDO_NOT_ALLOWED", again.Code);

        // Plata redevine nealocata: alocarea manuala o pune inapoi si TVA-ul ramas se face exigibil.
        var payment = await Query<long>("SELECT CreditOpenItemId FROM fin.Allocation WHERE Id = @Id", new { Id = last });
        await Send(new AllocateManually(partner, PartnerRole.Customer, [new AllocationPairInput(invoice, payment, "390")]));
        Assert.Equal(190m, await Query<decimal>("SELECT SUM(VatAmountRon) FROM fin.VatCashEntry WHERE OpenItemId = @Id", new { Id = invoice }));
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox
            WHERE EventType LIKE '%TvaLaIncasareExigibil' AND Payload LIKE @Item AND Payload LIKE '%"isReversal":true%' AND Payload LIKE '%"vatAmountRon":-62.26%'
            """, new { Item = $"%\"openItemId\":{invoice}[,}}]%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A8")]
    public async Task Paying_a_supplier_invoice_missing_from_spv_needs_confirmation_or_is_blocked()
    {
        var supplier = await NewPartner();
        var invoice = await Register(Purchase(supplier, 500m, Day, EInvoiceStatus.NotReceivedFromSpv));
        var warning = await Assert.ThrowsAsync<ErpException>(() => Pay(supplier, PartnerRole.Supplier, 500m, Day.AddDays(1), false, new SettlementTarget(invoice, null)));
        Assert.Equal("FIN_OPEN_ITEM_NOT_IN_SPV_UNCONFIRMED", warning.Code);
        var confirmed = await Pay(supplier, PartnerRole.Supplier, 200m, Day.AddDays(1), true, new SettlementTarget(invoice, null));
        Assert.Equal(200m, confirmed.AllocatedAmount);
        Assert.Equal((-500m, -300m), await Item(invoice));

        // FIFO nu atinge factura neprimita in SPV: plata fara factura indicata ramane nealocata.
        var unallocated = await Pay(supplier, PartnerRole.Supplier, 100m, Day.AddDays(2));
        Assert.Equal(0m, unallocated.AllocatedAmount);

        // Furnizorul din afara Romaniei e scutit.
        var foreign = await NewPartner("DE", 2);
        var foreignInvoice = await Register(Purchase(foreign, 80m, Day, EInvoiceStatus.NotReceivedFromSpv));
        Assert.Equal(80m, (await Pay(foreign, PartnerRole.Supplier, 80m, Day.AddDays(1), false, new SettlementTarget(foreignInvoice, null))).AllocatedAmount);

        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            await connection.ExecuteAsync("INSERT INTO fin.CompanySetting (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, 'Fin.Payment.RequireSpvInvoice', N'1', 1)",
                new { Company = Database.CompanyId });
        }

        try
        {
            var blocked = await Assert.ThrowsAsync<ErpException>(() => Pay(supplier, PartnerRole.Supplier, 100m, Day.AddDays(3), true, new SettlementTarget(invoice, null)));
            Assert.Equal("FIN_OPEN_ITEM_NOT_IN_SPV", blocked.Code);
        }
        finally
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.ExecuteAsync("DELETE FROM fin.CompanySetting WHERE CompanyId = @Company AND Code = 'Fin.Payment.RequireSpvInvoice'",
                new { Company = Database.CompanyId });
        }

        // Blocarea la plata a facturii (alt furnizor: plata nealocata de mai sus ar stinge factura noua prin FIFO).
        var blockedSupplier = await NewPartner();
        var other = await Register(Purchase(blockedSupplier, 90m, Day));
        var version = await Query<byte[]>("SELECT RowVersion FROM fin.OpenItem WHERE Id = @Id", new { Id = other });
        await Send(new SetOpenItemPaymentBlock(other, true, version));
        var block = await Assert.ThrowsAsync<ErpException>(() => Pay(blockedSupplier, PartnerRole.Supplier, 90m, Day.AddDays(4), true, new SettlementTarget(other, null)));
        Assert.Equal("FIN_OPEN_ITEM_BLOCKED_FOR_PAYMENT", block.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A4")]
    public async Task Two_simultaneous_payments_cannot_settle_an_invoice_beyond_its_value()
    {
        var partner = await NewPartner();
        await Send(new SavePartnerFinanceProfile(partner, PartnerRole.Customer, AutoAllocationMode.IndicatedOnly, "0", 0, false, false, false,
            null, null, null));
        var invoice = await Register(Sale(partner, 1000m, Day, Day.AddDays(10)));

        var results = await Task.WhenAll(
            Pay(partner, PartnerRole.Customer, 800m, Day.AddDays(1), false, new SettlementTarget(invoice, null)),
            Pay(partner, PartnerRole.Customer, 800m, Day.AddDays(1), false, new SettlementTarget(invoice, null)));

        Assert.Equal(1000m, results.Sum(r => r.AllocatedAmount));
        Assert.Equal(600m, results.Sum(r => r.UnallocatedAmount));
        Assert.Equal((1000m, 0m), await Item(invoice));
        Assert.Equal(1000m, await Query<decimal>("SELECT SUM(DebitAmount) FROM fin.Allocation WHERE DebitOpenItemId = @Id AND Status = 1", new { Id = invoice }));
    }

    [Fact]
    public async Task A_credit_note_settles_the_reversed_invoice_and_a_reversed_payment_reopens_its_invoices()
    {
        var partner = await NewPartner();
        var original = Sale(partner, 600m, Day, Day.AddDays(30));
        var invoice = await Register(original);
        var credit = await Register(Sale(partner, -200m, Day.AddDays(1), Day.AddDays(1), reversedId: original.InvoiceId));
        Assert.Equal((600m, 400m), await Item(invoice));
        Assert.Equal((-200m, 0m), await Item(credit));
        Assert.Equal((byte)4, await Query<byte>("SELECT Source FROM fin.Allocation WHERE CreditOpenItemId = @Id", new { Id = credit }));

        var payment = await Pay(partner, PartnerRole.Customer, 400m, Day.AddDays(2), false, new SettlementTarget(invoice, null));
        Assert.Equal((600m, 0m), await Item(invoice));
        var source = await Query<long>("SELECT SourceDocumentId FROM fin.OpenItem WHERE Id = @Id", new { Id = payment.PaymentOpenItemId });

        await InTransaction(async sp =>
        {
            await sp.GetRequiredService<ISettlementService>().ReversePaymentAsync(Database.CompanyId, "Finance.CashDocument", source, null, Day.AddDays(5), Ct);
            return true;
        });
        Assert.Equal((600m, 400m), await Item(invoice));
        Assert.Equal((-400m, 0m), await Item(payment.PaymentOpenItemId));
        Assert.Equal(0m, await Query<decimal>("SELECT SUM(OpenAmount) FROM fin.OpenItem WHERE SourceDocumentType = 'Finance.CashDocument' AND SourceDocumentId = @Id",
            new { Id = source }));
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox
            WHERE EventType LIKE '%FacturaAchitata' AND Payload LIKE @Document AND Payload LIKE '%"isReopened":true%'
            """, new { Document = $"%\"sourceDocumentId\":{original.InvoiceId}[,}}]%" }));

        // Soldul la o data trecuta refacut din alocari: inainte de plata, factura avea 400 rest.
        Assert.Equal(400m, Assert.Single(await Send(new GetPartnerBalance(partner, PartnerRole.Customer, Day.AddDays(1)))).Balance);
        // A doua stornare nu mai face nimic.
        await InTransaction(async sp =>
        {
            await sp.GetRequiredService<ISettlementService>().ReversePaymentAsync(Database.CompanyId, "Finance.CashDocument", source, null, Day.AddDays(6), Ct);
            return true;
        });
        Assert.Equal(2, await Query<int>("SELECT COUNT(*) FROM fin.OpenItem WHERE SourceDocumentType = 'Finance.CashDocument' AND SourceDocumentId = @Id",
            new { Id = source }));
    }

    [Fact]
    public async Task A_disputed_invoice_is_skipped_by_fifo_and_needs_confirmation_when_allocated_manually()
    {
        var partner = await NewPartner();
        var disputed = await Register(Sale(partner, 300m, Day, Day.AddDays(5)));
        var version = await Query<byte[]>("SELECT RowVersion FROM fin.OpenItem WHERE Id = @Id", new { Id = disputed });
        await Send(new SetOpenItemDispute(disputed, true, "Marfă neconformă", version));
        var payment = await Pay(partner, PartnerRole.Customer, 300m, Day.AddDays(1));
        Assert.Equal(0m, payment.AllocatedAmount);

        var unconfirmed = await Assert.ThrowsAsync<ErpException>(() => Send(new AllocateManually(partner, PartnerRole.Customer,
            [new AllocationPairInput(disputed, payment.PaymentOpenItemId, "300")])));
        Assert.Equal("FIN_OPEN_ITEM_DISPUTED_UNCONFIRMED", unconfirmed.Code);
        await Send(new AllocateManually(partner, PartnerRole.Customer, [new AllocationPairInput(disputed, payment.PaymentOpenItemId, "300")], true));
        Assert.Equal((300m, 0m), await Item(disputed));
    }

    [Fact]
    public async Task The_balance_list_shows_overdue_aging_and_unallocated_payments_and_the_sheet_lists_documents_and_allocations()
    {
        var partner = await NewPartner();
        var overdue = await Register(Sale(partner, 1000m, Day, Day.AddDays(10)));
        await Register(Sale(partner, 400m, Day.AddDays(30), Day.AddDays(60)));
        var payment = await Pay(partner, PartnerRole.Customer, 250m, Day.AddDays(35), false, new SettlementTarget(overdue, null));
        var advance = await Pay(partner, PartnerRole.Customer, 90m, Day.AddDays(36));
        Assert.Equal(90m, advance.AllocatedAmount); // FIFO pe restul facturii restante
        await Register(Purchase(partner, 300m, Day.AddDays(5)));

        var asOf = Day.AddDays(45); // prima factura e restanta de 35 de zile
        var list = await Send(new GetPartnerBalances(asOf, Search: (await Query<string>("SELECT Code FROM core.Partner WHERE Id = @Id", new { Id = partner }))));
        var customer = Assert.Single(list.Rows, r => r.PartnerId == partner && r.Role == PartnerRole.Customer);
        Assert.Equal(("1060.00", "660.00", "400.00", "660.00", 35, true), (customer.BalanceRon, customer.OverdueRon, customer.CurrentRon, customer.Days60Ron,
            customer.MaxOverdueDays, customer.HasOtherRole));
        Assert.Equal("-300.00", Assert.Single(list.Rows, r => r.PartnerId == partner && r.Role == PartnerRole.Supplier).BalanceRon);
        Assert.Contains((await Send(new GetPartnerBalances(asOf, PartnerRole.Customer, OnlyOverdue: true))).Rows, r => r.PartnerId == partner);
        Assert.DoesNotContain((await Send(new GetPartnerBalances(asOf, OnlyUnallocated: true))).Rows, r => r.PartnerId == partner);
        Assert.True(decimal.Parse(list.Totals.ReceivablesRon, System.Globalization.CultureInfo.InvariantCulture) >= 1060m);

        // La o data dinaintea platii, toata prima factura era restanta.
        var before = await Send(new GetPartnerBalances(Day.AddDays(20), PartnerRole.Customer));
        Assert.Equal("1000.00", Assert.Single(before.Rows, r => r.PartnerId == partner).OverdueRon);

        var documents = await Send(new GetOpenItems(partner, PartnerRole.Customer, OnlyOpen: false));
        Assert.Equal(4, documents.Count);
        Assert.Contains(documents, d => d.OpenItemId == payment.PaymentOpenItemId && d.ClosedAt == Day.AddDays(35));
        var allocations = await Send(new GetPartnerAllocations(partner));
        Assert.Equal(2, allocations.Count);
        Assert.All(allocations, a => Assert.Equal(overdue, a.DebitOpenItemId));
        Assert.Equal(("90.00", AllocationSource.AutomaticOnPayment), (allocations[0].Amount, allocations[0].Source));
    }

    private async Task<IEnumerable<T>> QueryAll<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.QueryAsync<T>(sql, parameters);
    }
}
