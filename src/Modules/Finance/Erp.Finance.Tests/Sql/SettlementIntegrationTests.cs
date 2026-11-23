using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Messaging;
using Erp.Finance.Application;
using Erp.Finance.Application.Fx;
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
        Erp.Core.Infrastructure.CoreInfrastructureRegistration.AddCoreInfrastructure(services, Database.ConnectionString,
            new Erp.Core.Infrastructure.Licensing.LicenseOptions(Development: true));
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

    [Fact]
    public async Task The_due_schedule_groups_receivables_and_payables_by_day_with_installments_and_overdue()
    {
        var partner = await NewPartner();
        var from = new DateOnly(2027, 3, 1);
        var installments = Sale(partner, 1000m, from.AddDays(-20), from.AddDays(2)) with
        {
            DueDates = [new InvoiceDueDate(from.AddDays(-5), 400m), new InvoiceDueDate(from.AddDays(2), 600m)],
        };
        var invoice = await Register(installments);
        var supplierInvoice = await Register(Purchase(partner, 300m, from.AddDays(-28))); // scadenta from + 2
        await Pay(partner, PartnerRole.Customer, 100m, from.AddDays(-1), false, new SettlementTarget(invoice, null));

        var schedule = await Send(new GetDueSchedule(from, from.AddDays(6)));
        var overdue = Assert.Single(schedule.Days, d => d.Day is null);
        Assert.True(overdue.ReceivableCount >= 1);
        var day = Assert.Single(schedule.Days, d => d.Day == from.AddDays(2));
        Assert.True(day.ReceivableCount >= 1 && day.PayableCount >= 1);

        var dayItems = await Send(new GetDueItems(PartnerRole.Customer, from.AddDays(2)));
        Assert.Equal("600.00", Assert.Single(dayItems, i => i.OpenItemId == invoice).OpenAmount);
        var late = await Send(new GetDueItems(PartnerRole.Customer, from, Overdue: true));
        Assert.Equal("300.00", Assert.Single(late, i => i.OpenItemId == invoice).OpenAmount); // prima rata, dupa plata de 100
        var payables = await Send(new GetDueItems(PartnerRole.Supplier, from.AddDays(2)));
        Assert.Equal(("300.00", false), (Assert.Single(payables, i => i.OpenItemId == supplierInvoice).OpenAmount, payables[0].IsBlockedForPayment));

        var tooLong = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new GetDueSchedule(from, from.AddDays(200))));
        Assert.Contains("to", tooLong.Errors.Keys);
    }

    [Fact]
    public async Task A_small_difference_is_written_off_under_the_threshold_with_vat_on_collection_and_needs_the_right_above_it()
    {
        Permissions.Add(FinancePermissions.WriteOffCreate);
        var partner = await NewPartner();
        var invoice = await Register(Sale(partner, 1190m, Day, Day.AddDays(30), vat: 190m, vatOnCollection: true));
        await Pay(partner, PartnerRole.Customer, 1186.50m, Day.AddDays(1), false, new SettlementTarget(invoice, null));
        Assert.Equal((1190m, 3.50m), await Item(invoice));

        var ids = await Send(new CreateWriteOffs([new WriteOffInput(invoice, null)], WriteOffReason.FeeWithheldByPartner, "Comision bancar reținut"));
        Assert.Equal((1190m, 0m), await Item(invoice));
        Assert.Equal(190m, await Query<decimal>("SELECT SUM(VatAmountRon) FROM fin.VatCashEntry WHERE OpenItemId = @Id", new { Id = invoice }));
        Assert.Equal((byte)6, await Query<byte>("SELECT Source FROM fin.Allocation WHERE WriteOffId = @Id", new { Id = Assert.Single(ids) }));
        Assert.Equal((-3.50m, 0m, (byte)9), await Query<(decimal, decimal, byte)>(
            "SELECT o.Amount, o.OpenAmount, o.Kind FROM fin.WriteOff AS w JOIN fin.OpenItem AS o ON o.Id = w.WriteOffOpenItemId WHERE w.Id = @Id", new { Id = ids[0] }));

        // Alocarea unei ajustari nu se desface direct.
        var allocation = await Query<long>("SELECT Id FROM fin.Allocation WHERE WriteOffId = @Id", new { Id = ids[0] });
        var undo = await Assert.ThrowsAsync<ErpException>(() => Send(new UndoAllocation(allocation, "greșit")));
        Assert.Equal("FIN_ALLOCATION_UNDO_NOT_ALLOWED", undo.Code);

        // Peste prag: refuzata fara permisiunea dedicata; cu ea, trece. O suma mai mare decat restul e refuzata.
        var big = await Register(Sale(partner, 50m, Day, Day.AddDays(30)));
        var above = await Assert.ThrowsAsync<ErpException>(() => Send(new CreateWriteOffs([new WriteOffInput(big, "20")], WriteOffReason.AcceptedDifference)));
        Assert.Equal("FIN_WRITEOFF_ABOVE_THRESHOLD", above.Code);
        var exceeds = await Assert.ThrowsAsync<ErpException>(() => Send(new CreateWriteOffs([new WriteOffInput(big, "60")], WriteOffReason.AcceptedDifference)));
        Assert.Equal("FIN_ALLOCATION_EXCEEDS_OPEN", exceeds.Code);
        Permissions.Add(FinancePermissions.WriteOffAboveThreshold);
        await Send(new CreateWriteOffs([new WriteOffInput(big, "20")], WriteOffReason.AcceptedDifference));
        Assert.Equal((50m, 30m), await Item(big));
        Assert.True(await Query<bool>("SELECT AboveThreshold FROM fin.WriteOff WHERE OpenItemId = @Id", new { Id = big }));
    }

    [Fact]
    public async Task Attachments_are_stored_with_their_hash_checked_by_type_and_kept_per_company()
    {
        var pdf = "%PDF-1.4\n1 0 obj\n"u8.ToArray();
        var id = await InTransaction(sp => sp.GetRequiredService<IAttachmentStore>().SaveAsync("Finance.Compensation", 4242, "PV semnat.pdf", "text/plain",
            new MemoryStream(pdf), Ct));
        var (info, content) = await InTransaction(async sp =>
        {
            var store = sp.GetRequiredService<IAttachmentStore>();
            await using var stream = await store.OpenAsync(id, Ct);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, Ct);
            return (await store.GetInfoAsync(id, Ct), copy.ToArray());
        });
        Assert.Equal(("PV semnat.pdf", "application/pdf", (long)pdf.Length), (info!.FileName, info.ContentType, info.Size));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pdf)), info.Sha256);
        Assert.Equal(pdf, content);
        Assert.Single(await InTransaction(sp => sp.GetRequiredService<IAttachmentStore>().ListAsync("Finance.Compensation", 4242, Ct)), a => a.Id == id);

        // Alta firma nu il vede; un executabil sau un PDF fals sunt refuzate.
        Assert.Null(await InTransaction(sp => sp.GetRequiredService<IAttachmentStore>().GetInfoAsync(id, Ct), Database.OtherCompanyId));
        var exe = await Assert.ThrowsAsync<ErpException>(() => InTransaction(sp => sp.GetRequiredService<IAttachmentStore>().SaveAsync("Finance.Compensation",
            4242, "pv.exe", "application/pdf", new MemoryStream(pdf), Ct)));
        Assert.Equal("CORE_ATTACHMENT_INVALID", exe.Code);
        var fake = await Assert.ThrowsAsync<ErpException>(() => InTransaction(sp => sp.GetRequiredService<IAttachmentStore>().SaveAsync("Finance.Compensation",
            4242, "pv.pdf", "application/pdf", new MemoryStream("MZ"u8.ToArray()), Ct)));
        Assert.Equal("CORE_ATTACHMENT_INVALID", fake.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A9")]
    public async Task A_balanced_compensation_settles_both_sides_with_vat_on_collection_and_its_reversal_restores_the_balances()
    {
        Permissions.UnionWith([FinancePermissions.CompensationCreate, FinancePermissions.CompensationValidate]);
        Erp.Pdf.PdfSetup.Configure(null);
        var partner = await NewPartner();
        var sale = await Register(Sale(partner, 1190m, Day, Day.AddDays(10), vat: 190m, vatOnCollection: true));
        var sale2 = await Register(Sale(partner, 300m, Day.AddDays(1), Day.AddDays(20)));
        var purchase = await Register(Purchase(partner, 1000m, Day.AddDays(2)));

        Assert.Contains(await Send(new GetCompensationProposals()), p => p.PartnerId == partner && p.Amount == "1000.00");
        var proposed = await Send(new ProposeCompensation(partner, "RON"));
        Assert.Equal([(sale, "1000.00"), (purchase, "-1000.00")], proposed.Select(l => (l.OpenItemId, l.Amount)));

        // Neechilibrat: refuzat la validare.
        var unbalanced = await Send(new SaveCompensation(null, Day.AddDays(5), CompensationKind.SamePartner, null, "RON", null,
            [new CompensationLineInput(sale2, "300"), new CompensationLineInput(purchase, "-250")], null));
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidateCompensation(unbalanced.Id, unbalanced.RowVersion)));
        Assert.Equal("FIN_COMPENSATION_NOT_BALANCED", refused.Code);
        await Send(new DeleteCompensation(unbalanced.Id, unbalanced.RowVersion));

        var draft = await Send(new SaveCompensation(null, Day.AddDays(5), CompensationKind.SamePartner, null, "RON", "Compensare lunară", proposed, null));
        var pdf = await Send(new PrintCompensation(draft.Id));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
        var validated = await Send(new ValidateCompensation(draft.Id, draft.RowVersion));
        var detail = await Send(new GetCompensation(draft.Id));
        Assert.Equal((CompensationStatus.Validated, "1000.00"), (detail.Status, detail.TotalAmount));
        Assert.NotNull(detail.Number);
        Assert.Equal((1190m, 190m), await Item(sale));
        Assert.Equal((-1000m, 0m), await Item(purchase));
        // TVA la incasare: compensarea conteaza ca incasare (1000 din 1190 → 159,66).
        Assert.Equal(159.66m, await Query<decimal>("SELECT SUM(VatAmountRon) FROM fin.VatCashEntry WHERE OpenItemId = @Id", new { Id = sale }));
        Assert.Equal(2, await Query<int>("SELECT COUNT(*) FROM fin.OpenItem WHERE SourceDocumentType = 'Finance.Compensation' AND SourceDocumentId = @Id AND OpenAmount = 0",
            new { draft.Id }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%CompensareValidata' AND Payload LIKE @Like",
            new { Like = $"%\"compensationId\":{draft.Id}[,}}]%" }));

        // PV-ul semnat, scanat.
        var signed = await Send(new AttachCompensationScan(draft.Id, "pv semnat.pdf", "%PDF-1.4 scan"u8.ToArray()));
        Assert.Equal("pv semnat.pdf", (await Send(new GetCompensationScan(draft.Id))).FileName);
        Assert.DoesNotContain(await Send(new GetCompensations(WithoutSigned: true)), c => c.Id == draft.Id);

        // Stornarea readuce resturile si TVA-ul; partidele tehnice se sting intre ele.
        await Send(new ReverseCompensation(draft.Id, signed.RowVersion));
        Assert.Equal((1190m, 1190m), await Item(sale));
        Assert.Equal((-1000m, -1000m), await Item(purchase));
        Assert.Equal(0m, await Query<decimal>("SELECT SUM(VatAmountRon) FROM fin.VatCashEntry WHERE OpenItemId = @Id", new { Id = sale }));
        Assert.Equal(0m, await Query<decimal>("SELECT SUM(OpenAmount) FROM fin.OpenItem WHERE SourceDocumentType = 'Finance.Compensation' AND SourceDocumentId = @Id",
            new { draft.Id }));
        Assert.Equal(CompensationStatus.Reversed, (await Send(new GetCompensation(draft.Id))).Status);
        _ = validated;
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A10")]
    public async Task A_refused_cheque_reopens_the_invoice_settled_on_receipt_and_an_endorsed_note_pays_a_supplier()
    {
        Permissions.Add(FinancePermissions.CommercialPaperManage);
        var customer = await NewPartner();
        var invoice = await Register(Sale(customer, 6450m, Day, Day.AddDays(10)));
        var series = Unique("BRD");
        var saved = await Send(new RegisterCommercialPaper(customer, PaperDirection.Received, PaperKind.Cheque, series, "1188306", "BRD", null, "6450", "RON",
            Day.AddDays(5), Day.AddDays(5), null, [invoice]));
        Assert.Equal((6450m, 0m), await Item(invoice));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new RegisterCommercialPaper(customer, PaperDirection.Received, PaperKind.Cheque, series,
            "1188306", null, null, "10", "RON", Day.AddDays(5), Day.AddDays(5), null, [])));
        Assert.Equal("FIN_COMMERCIAL_PAPER_DUPLICATE", duplicate.Code);

        var (account, _) = await NewBankAccount();
        var deposited = await Send(new ChangeCommercialPaperStatus(saved.Id, PaperStatus.Deposited, Day.AddDays(6), account, null, null, null, null, saved.RowVersion));
        var wrong = await Assert.ThrowsAsync<ErpException>(() => Send(new ChangeCommercialPaperStatus(saved.Id, PaperStatus.Endorsed, Day.AddDays(7), null, null,
            customer, null, null, deposited.RowVersion)));
        Assert.Equal("FIN_COMMERCIAL_PAPER_STATUS_INVALID", wrong.Code);

        await Send(new ChangeCommercialPaperStatus(saved.Id, PaperStatus.Refused, Day.AddDays(8), null, null, null, null, "Lipsă disponibil", deposited.RowVersion));
        Assert.Equal((6450m, 6450m), await Item(invoice));
        var detail = await Send(new GetCommercialPaper(saved.Id));
        Assert.Equal((PaperStatus.Refused, 3), (detail.Status, detail.Events.Count));
        Assert.Equal(0m, await Query<decimal>("SELECT SUM(OpenAmount) FROM fin.OpenItem WHERE SourceDocumentType = 'Finance.CommercialPaper' AND SourceDocumentId = @Id",
            new { saved.Id }));
        Assert.Equal(3, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%EfectComercialModificat' AND Payload LIKE @Like",
            new { Like = $"%\"commercialPaperId\":{saved.Id}[,}}]%" }));

        // Un bilet la ordin primit si girat catre un furnizor: stinge factura clientului la primire si factura furnizorului la gir.
        var invoice2 = await Register(Sale(customer, 1000m, Day, Day.AddDays(30)));
        var note = await Send(new RegisterCommercialPaper(customer, PaperDirection.Received, PaperKind.PromissoryNote, Unique("BT"), "4471203", "BT", null, "1000", "RON",
            Day.AddDays(1), Day.AddDays(40), null, [invoice2]));
        var supplier = await NewPartner();
        var supplierInvoice = await Register(Purchase(supplier, 1000m, Day));
        await Send(new ChangeCommercialPaperStatus(note.Id, PaperStatus.Endorsed, Day.AddDays(2), null, null, supplier, [supplierInvoice], null, note.RowVersion));
        Assert.Equal((0m, 0m), ((await Item(invoice2)).Open, (await Item(supplierInvoice)).Open));
        Assert.Contains((await Send(new GetCommercialPapers(PaperDirection.Received))).Items, p => p.Id == note.Id && p.Status == PaperStatus.Endorsed);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A12")]
    public async Task A_balance_confirmation_lists_the_items_open_at_its_date_goes_out_by_email_and_records_a_contested_answer()
    {
        Permissions.Add(FinancePermissions.BalanceConfirmationManage);
        Erp.Pdf.PdfSetup.Configure(null);
        var partner = await NewPartner();
        var small = await NewPartner();
        var sale = await Register(Sale(partner, 1000m, Day, Day.AddDays(10)));
        var later = await Register(Sale(partner, 500m, Day.AddDays(10), Day.AddDays(20)));
        await Register(Sale(small, 40m, Day, Day.AddDays(10)));
        await Pay(partner, PartnerRole.Customer, 600m, Day.AddDays(5), targets: new SettlementTarget(sale, 600m));
        // O plata de dupa data confirmarii nu schimba partidele de la acea data.
        await Pay(partner, PartnerRole.Customer, 400m, Day.AddDays(12), targets: new SettlementTarget(sale, 400m));
        var asOf = Day.AddDays(7);

        // F3-A12: partidele refacute la data trecuta.
        var items = await InTransaction(sp => sp.GetRequiredService<IBalanceConfirmationStore>().OpenItemsAsOfAsync(Database.CompanyId, partner, PartnerRole.Customer,
            asOf, Ct));
        Assert.Equal([(sale, 400m)], items.Select(i => (i.OpenItemId, i.OpenAmount)));
        Assert.DoesNotContain(items, i => i.OpenItemId == later);

        // Lotul: pragul de 100 lei scoate partenerul mic; PDF-ul e generat.
        var batchId = await Send(new GenerateBalanceConfirmations(asOf, PartnerRole.Customer, "100", [partner, small]));
        var list = await Send(new GetBalanceConfirmations(batchId));
        var confirmation = Assert.Single(list.Items);
        Assert.Equal((partner, "400.00", ConfirmationStatus.Generated), (confirmation.PartnerId, confirmation.OurBalance, confirmation.Status));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await Send(new GetBalanceConfirmationPdf(confirmation.Id))).Content, 0, 4));

        // Fara adresa nu pleaca; cu adresa, emailul intra in outbox cu PDF-ul atasat.
        var none = await Send(new SendBalanceConfirmations(batchId, false));
        Assert.Equal((0, 1), (none.Sent, none.WithoutEmail.Count));
        await Query<int>("UPDATE core.Partner SET Email = N'contabilitate@partener.ro' WHERE Id = @Id; SELECT @@ROWCOUNT", new { Id = partner });
        var sent = await Send(new SendBalanceConfirmations(batchId, false));
        Assert.Equal(1, sent.Sent);
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.EmailMessage AS m JOIN core.EmailMessageAttachment AS a ON a.EmailMessageId = m.Id
            WHERE m.EntityType = 'Finance.BalanceConfirmation' AND m.EntityId = @Id AND m.ToAddress = N'contabilitate@partener.ro'
            """, new { confirmation.Id }));
        confirmation = Assert.Single((await Send(new GetBalanceConfirmations(batchId))).Items);
        Assert.Equal((ConfirmationStatus.Sent, "contabilitate@partener.ro"), (confirmation.Status, confirmation.SentTo));

        // Contestarea cere soldul partenerului si explicatia; diferenta se calculeaza.
        var invalid = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new RecordBalanceConfirmationResponse(confirmation.Id, ConfirmationStatus.Contested,
            asOf.AddDays(20), null, null, false, null, null, confirmation.RowVersion)));
        Assert.Contains("partnerBalance", invalid.Errors.Keys);
        await Send(new RecordBalanceConfirmationResponse(confirmation.Id, ConfirmationStatus.Contested, asOf.AddDays(20), "300", "Plata din 06.10 lipsește la noi",
            false, "raspuns.pdf", "%PDF-1.4 raspuns"u8.ToArray(), confirmation.RowVersion));
        var answered = Assert.Single((await Send(new GetBalanceConfirmations(batchId))).Items);
        Assert.Equal((ConfirmationStatus.Contested, "300.00", "-100.00"), (answered.Status, answered.PartnerBalance, answered.Difference));
        Assert.Equal("raspuns.pdf", (await Send(new GetBalanceConfirmationResponseFile(confirmation.Id))).FileName);
        Assert.Equal((1, 1, 0), (list.Batches[0].Total, (await Send(new GetBalanceConfirmations(batchId))).Batches.Single(b => b.Id == batchId).Contested, 0));
        // Confirmarile cu raspuns nu se mai retrimit.
        Assert.Equal(0, (await Send(new SendBalanceConfirmations(batchId, true))).Sent);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A11")]
    public async Task Penalties_follow_the_contract_or_the_legal_rate_skip_disputed_items_and_approval_publishes_them()
    {
        Permissions.UnionWith([FinancePermissions.PenaltyCalculate, FinancePermissions.PenaltyApprove, FinancePermissions.TreasuryConfigure]);
        await Send(new SaveReferenceRate(new DateOnly(2026, 1, 1), "6.75"));
        await Send(new SaveReferenceRate(new DateOnly(2026, 7, 1), "6.5"));
        Assert.Contains(await Send(new GetReferenceRates()), r => r.ValidFrom == new DateOnly(2026, 7, 1) && r.Rate == "6.5");
        var (from, to) = (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        // Contract 0,10%/zi cu suma fixa de 40 EUR; 400 platiti pe 10.10: doua intervale cu baze diferite.
        var contract = await NewPartner();
        var invoice = await Register(Sale(contract, 1000m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        await Query<int>("""
            UPDATE fin.PartnerFinanceProfile SET PenaltyRatePerDay = 0.1, ApplyFixedRecoveryCost = 1 WHERE CompanyId = @CompanyId AND PartnerId = @PartnerId AND Role = 1;
            SELECT @@ROWCOUNT
            """, new { Database.CompanyId, PartnerId = contract });
        await Pay(contract, PartnerRole.Customer, 400m, new DateOnly(2026, 10, 10), targets: new SettlementTarget(invoice, 400m));
        var draft = await Send(new CalculatePenalties(from, to, contract));
        var calculation = await Send(new GetPenaltyCalculation(draft.Id));
        Assert.Equal([(PenaltyLineKind.Interval, 10, "1000.00", "10.00", "RON"), (PenaltyLineKind.Interval, 21, "600.00", "12.60", "RON"),
            (PenaltyLineKind.FixedRecoveryCost, 0, "1000.00", "40.00", "EUR")],
            calculation.Lines.Select(l => (l.Kind, l.Days, l.BaseAmount, l.PenaltyAmount, l.CurrencyCode)));
        Assert.Equal([("RON", "22.60"), ("EUR", "40.00")], calculation.Totals.Select(t => (t.CurrencyCode, t.Total)));

        // Linia scoasa dispare; recalcularea o reface.
        var removed = await Send(new RemovePenaltyLines(draft.Id, [calculation.Lines[2].Id], calculation.RowVersion));
        Assert.Equal(2, (await Send(new GetPenaltyCalculation(draft.Id))).Lines.Count);
        var recalculated = await Send(new RecalculatePenalties(draft.Id, removed.RowVersion));
        Assert.Equal(3, (await Send(new GetPenaltyCalculation(draft.Id))).Lines.Count);

        await Send(new ApprovePenaltyCalculation(draft.Id, recalculated.RowVersion));
        Assert.Equal(PenaltyStatus.Approved, (await Send(new GetPenaltyCalculation(draft.Id))).Status);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%PenalitatiCalculate' AND Payload LIKE @Like",
            new { Like = $"%\"penaltyCalculationId\":{draft.Id}[,}}]%" }));

        // Aceeasi perioada nu se mai penalizeaza o data; un calcul gol nu se aproba si se anuleaza.
        var again = await Send(new CalculatePenalties(from, to, contract));
        var empty = await Send(new GetPenaltyCalculation(again.Id));
        Assert.Empty(empty.Lines);
        Assert.Equal("FIN_PENALTY_EMPTY", (await Assert.ThrowsAsync<ErpException>(() => Send(new ApprovePenaltyCalculation(again.Id, empty.RowVersion)))).Code);
        await Send(new CancelPenaltyCalculation(again.Id, empty.RowVersion));
        var cancelled = await Send(new GetPenaltyCalculation(again.Id));
        Assert.Equal("FIN_PENALTY_NOT_DRAFT", (await Assert.ThrowsAsync<ErpException>(() => Send(new CancelPenaltyCalculation(again.Id, cancelled.RowVersion)))).Code);

        // Fara rata in contract: dobanda legala 6,50% + 8 = 14,50% pe an; factura in litigiu se exclude si se numara.
        var legal = await NewPartner();
        await Register(Sale(legal, 2000m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        var disputed = await Register(Sale(legal, 500m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        await Query<int>("UPDATE fin.OpenItem SET IsDisputed = 1 WHERE Id = @Id; SELECT @@ROWCOUNT", new { Id = disputed });
        var legalCalculation = await Send(new GetPenaltyCalculation((await Send(new CalculatePenalties(from, to, legal))).Id));
        var line = Assert.Single(legalCalculation.Lines);
        Assert.Equal((31, "14.5", "24.63", 1), (line.Days, line.AnnualRate, line.PenaltyAmount, legalCalculation.DisputedCount));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A13")]
    public async Task An_opening_balance_import_of_50000_invoices_has_no_duplicates_and_its_totals_per_role_match_the_file()
    {
        Permissions.Add(FinancePermissions.OpeningBalanceImport);
        var company = Database.ImportCompanyId;
        var prefix = Unique("SI");
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
                SELECT CONCAT(@Prefix, '-', n), CONCAT(N'Partener sold inițial ', n), 1, CAST(@Base + n AS nvarchar(20)), 1, 1, 1, N'Str. 1', N'Iași', N'IS', 'RO', 1, 1
                FROM (SELECT TOP (50) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects) AS x
                """, new { Prefix = prefix, Base = Random.Shared.Next(10_000_000, 89_999_000) });
        }

        // 50.000 de randuri: facturi si plati nealocate, client si furnizor, cateva in EUR.
        var rows = new List<IReadOnlyList<string?>>();
        var expected = new Dictionary<PartnerRole, decimal> { [PartnerRole.Customer] = 0m, [PartnerRole.Supplier] = 0m };
        for (var i = 0; i < 50_000; i++)
        {
            var role = i % 3 == 0 ? PartnerRole.Supplier : PartnerRole.Customer;
            var payment = i % 10 == 0;
            var eur = i % 25 == 0;
            var amount = 100m + (i % 900) + 0.5m;
            var open = amount - (i % 7);
            var rate = eur ? 4.9750m : 1m;
            var debit = (role == PartnerRole.Customer) != payment;
            expected[role] += (debit ? 1 : -1) * Math.Round(open * rate, 2, MidpointRounding.AwayFromZero);
            var date = new DateOnly(2026, 6, 30).AddDays(-(i % 100));
            rows.Add([$"{prefix}-{i % 50 + 1}", role == PartnerRole.Customer ? "Client" : "Furnizor", payment ? "Plată nealocată" : "Factură", $"SI-{i}",
                date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                date.AddDays(30).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture), eur ? "EUR" : "RON", eur ? "4,9750" : null,
                amount.ToString(System.Globalization.CultureInfo.InvariantCulture), open.ToString(System.Globalization.CultureInfo.InvariantCulture), "Nu", null]);
        }

        var file = await InTransaction(sp => Task.FromResult(sp.GetRequiredService<ISpreadsheetFiles>().Write(OpeningSheet.SheetName, OpeningSheet.Columns, rows,
            OpeningSheet.Lists)), company);
        var upload = await SendTo(company, new UploadOpeningBalances("sold initial.xlsx", Convert.ToBase64String(file)));
        Assert.Equal((50_000, 50_000, 0, 0), (upload.TotalRows, upload.ValidRows, upload.ErrorRows, upload.ExistingRows));
        Assert.Equal([("RON-Customer", expected[PartnerRole.Customer]), ("RON-Supplier", expected[PartnerRole.Supplier])],
            upload.Totals.GroupBy(t => t.Role).Select(g => ($"RON-{g.Key}", g.Sum(t => decimal.Parse(t.TotalRon, System.Globalization.CultureInfo.InvariantCulture)))));

        while (!(await SendTo(company, new ImportOpeningOpenItems(upload.JobId))).Done)
        {
        }

        Assert.Equal((ImportStatus.Imported, 50_000), ((await SendTo(company, new GetOpeningBalanceImport(upload.JobId))).Status, (await SendTo(company, new GetOpeningBalanceImport(upload.JobId))).ImportedRows));
        const string Totals = """
            SELECT o.Role, COUNT(*) AS Items, SUM(o.AmountRon) AS TotalRon, SUM(CASE WHEN o.OpenAmount = o.Amount THEN 0 ELSE 1 END) AS NotOpen
            FROM fin.OpenItem AS o JOIN core.Partner AS p ON p.Id = o.PartnerId
            WHERE o.SourceDocumentType = 'Finance.OpeningBalance' AND p.Code LIKE @Like AND o.CompanyId = @CompanyId
            GROUP BY o.Role ORDER BY o.Role
            """;
        var imported = (await QueryAll<(byte Role, int Items, decimal TotalRon, int NotOpen)>(Totals, new { Like = prefix + "-%", CompanyId = company })).ToList();
        Assert.Equal(50_000, imported.Sum(r => r.Items));
        Assert.Equal([((byte)1, expected[PartnerRole.Customer]), ((byte)2, expected[PartnerRole.Supplier])], imported.Select(r => (r.Role, r.TotalRon)));
        Assert.All(imported, r => Assert.Equal(0, r.NotOpen));

        // Reluarea aceluiasi fisier: toate randurile sunt deja importate, nimic nu se dubleaza.
        var again = await SendTo(company, new UploadOpeningBalances("sold initial.xlsx", Convert.ToBase64String(file)));
        Assert.Equal((0, 50_000), (again.ValidRows, again.ExistingRows));
        Assert.True((await SendTo(company, new ImportOpeningOpenItems(again.JobId))).Done);
        Assert.Equal(50_000, (await QueryAll<(byte Role, int Items, decimal TotalRon, int NotOpen)>(Totals, new { Like = prefix + "-%", CompanyId = company }))
            .Sum(r => r.Items));

        // Randurile gresite raman in raport si se descarca, cu coloana „Eroare”.
        var bad = await InTransaction(sp => Task.FromResult(sp.GetRequiredService<ISpreadsheetFiles>().Write(OpeningSheet.SheetName, OpeningSheet.Columns,
        [
            ["NU-EXISTA", "Client", "Factură", "X-1", "2026-06-30", null, "RON", null, "100", "50", "Nu", null],
            [$"{prefix}-1", "Client", "Factură", "X-2", "2026-06-30", null, "RON", null, "100", "150", "Nu", null],
            [$"{prefix}-1", "Furnizor", "Factură", "X-3", "30.06.2026", null, "EUR", null, "100", "100", "Da", "5"],
        ], OpeningSheet.Lists)), company);
        var checkedFile = await SendTo(company, new UploadOpeningBalances("gresit.xlsx", Convert.ToBase64String(bad)));
        Assert.Equal((3, 0, 3), (checkedFile.TotalRows, checkedFile.ValidRows, checkedFile.ErrorRows));
        Assert.Contains(checkedFile.Errors, e => e.Message.Contains("Partenerul nu există", StringComparison.Ordinal));
        Assert.Contains(checkedFile.Errors, e => e.Message.Contains("Restul nu poate depăși", StringComparison.Ordinal));
        Assert.Contains(checkedFile.Errors, e => e.Message.Contains("Cursul este obligatoriu", StringComparison.Ordinal));
        Assert.StartsWith("PK", System.Text.Encoding.ASCII.GetString((await SendTo(company, new DownloadOpeningBalanceErrors(checkedFile.JobId))).Content, 0, 2));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F4-A4")]
    [Trait("Criteriu", "02.02-F4-A12")]
    [Trait("Criteriu", "02.02-F4-A15")]
    public async Task An_eur_invoice_paid_in_lei_uses_the_partner_cross_rate_rule_or_the_operator_rate_and_undoing_reverses_the_difference()
    {
        Permissions.Add(FinancePermissions.FxView);
        var partner = await NewPartner();
        var (invoiceDate, payDay) = (new DateOnly(2025, 2, 3), new DateOnly(2025, 2, 10));
        await Query<int>("""
            MERGE core.ExchangeRate AS t USING (SELECT 'EUR' AS CurrencyCode, @Day AS RateDate) AS s ON t.CurrencyCode = s.CurrencyCode AND t.RateDate = s.RateDate
            WHEN MATCHED THEN UPDATE SET Rate = 5.0
            WHEN NOT MATCHED THEN INSERT (CurrencyCode, RateDate, Rate, Multiplier, Source, CreatedBy) VALUES ('EUR', @Day, 5.0, 1, 'BNR', 1);
            SELECT @@ROWCOUNT
            """, new { Day = payDay.ToDateTime(TimeOnly.MinValue) });
        var eur = Sale(partner, 1000m, invoiceDate, invoiceDate.AddDays(30)) with { CurrencyCode = "EUR", ExchangeRate = 4.95m, TotalAmountRon = 4950m };
        var invoice = await Register(eur);
        await Query<int>("""
            UPDATE fin.PartnerFinanceProfile SET CrossCurrencyRule = 2, CrossCurrencyMarkupPercent = 1 WHERE CompanyId = @CompanyId AND PartnerId = @PartnerId AND Role = 1;
            SELECT @@ROWCOUNT
            """, new { Database.CompanyId, PartnerId = partner });

        // A4: 6.000 RON pe factura de 1.000 EUR, la BNR 5,00 + 1% = 5,05: se sting 1.000 EUR cu 5.050 RON, castig 100; restul de 950 RON ramane nealocat.
        var paid = await Pay(partner, PartnerRole.Customer, 6000m, payDay, false, new SettlementTarget(invoice, null));
        Assert.Equal((5050m, 950m), (paid.AllocatedAmount, paid.UnallocatedAmount));
        var allocation = await Query<(long Id, decimal DebitAmount, decimal CreditAmount, decimal CrossRate, byte CrossRateSource)>(
            "SELECT Id, DebitAmount, CreditAmount, CrossRate, CrossRateSource FROM fin.Allocation WHERE DebitOpenItemId = @Id AND Status = 1", new { Id = invoice });
        Assert.Equal((1000m, 5050m, 5.05m, (byte)2), (allocation.DebitAmount, allocation.CreditAmount, allocation.CrossRate, allocation.CrossRateSource));
        Assert.Equal((100m, "765", 4950m, 5050m), await Query<(decimal, string, decimal, decimal)>(
            "SELECT DifferenceRon, GlAccountCode, CarryingAmountRon, SettlementAmountRon FROM fin.FxDifference WHERE AllocationId = @Id", new { allocation.Id }));
        Assert.Equal((0m, 0m), await Query<(decimal, decimal)>("SELECT OpenAmount, OpenAmountRon FROM fin.OpenItem WHERE Id = @Id", new { Id = invoice }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%DiferentaCursInregistrata' AND Payload LIKE @Like",
            new { Like = $"%\"allocationId\":{allocation.Id}[,}}]%" }));

        // A12: desfacerea scrie diferenta inversa si reface valorile contabile ale ambelor partide.
        Permissions.UnionWith([FinancePermissions.AllocationUndo, FinancePermissions.AllocationManage]);
        await Send(new UndoAllocation(allocation.Id, "Test"));
        Assert.Equal((-100m, true), await Query<(decimal, bool)>(
            "SELECT DifferenceRon, IsReversal FROM fin.FxDifference WHERE AllocationId = @Id AND ReversalOfId IS NOT NULL", new { allocation.Id }));
        Assert.Equal((1000m, 4950m), await Query<(decimal, decimal)>("SELECT OpenAmount, OpenAmountRon FROM fin.OpenItem WHERE Id = @Id", new { Id = invoice }));
        var payment = await Query<long>("SELECT CreditOpenItemId FROM fin.Allocation WHERE Id = @Id", new { allocation.Id });
        Assert.Equal((-6000m, -6000m), await Query<(decimal, decimal)>("SELECT OpenAmount, OpenAmountRon FROM fin.OpenItem WHERE Id = @Id", new { Id = payment }));

        // A15: cursul ales de operator cere dreptul dedicat, se memoreaza pe alocare si da diferenta.
        var pair = new AllocationPairInput(invoice, payment, "5100", "5.1");
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new AllocateManually(partner, PartnerRole.Customer, [pair])));
        Assert.Equal("FIN_FX_MANUAL_RATE_FORBIDDEN", refused.Code);
        Permissions.Add(FinancePermissions.FxManualCrossRate);
        await Send(new AllocateManually(partner, PartnerRole.Customer, [pair]));
        Assert.Equal((5.1m, (byte)4, 150m), await Query<(decimal, byte, decimal)>("""
            SELECT a.CrossRate, a.CrossRateSource, x.DifferenceRon FROM fin.Allocation AS a JOIN fin.FxDifference AS x ON x.AllocationId = a.Id
            WHERE a.DebitOpenItemId = @Id AND a.Status = 1
            """, new { Id = invoice }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F4-A1")]
    [Trait("Criteriu", "02.02-F4-A7")]
    [Trait("Criteriu", "02.02-F4-A8")]
    [Trait("Criteriu", "02.02-F4-A9")]
    public async Task The_monthly_revaluation_uses_month_end_balances_corrects_later_payments_and_can_be_reversed_exactly()
    {
        Permissions.UnionWith([FinancePermissions.FxView, FinancePermissions.FxRevalue, FinancePermissions.FxReverseRevaluation]);
        var company = Database.FxCompanyId;
        foreach (var (day, rate) in new[] { (new DateOnly(2024, 1, 31), 4.97m), (new DateOnly(2024, 2, 29), 4.98m) })
        {
            await Query<int>("""
                MERGE core.ExchangeRate AS t USING (SELECT 'EUR' AS CurrencyCode, @Day AS RateDate) AS s ON t.CurrencyCode = s.CurrencyCode AND t.RateDate = s.RateDate
                WHEN MATCHED THEN UPDATE SET Rate = @Rate
                WHEN NOT MATCHED THEN INSERT (CurrencyCode, RateDate, Rate, Multiplier, Source, CreatedBy) VALUES ('EUR', @Day, @Rate, 1, 'BNR', 1);
                SELECT @@ROWCOUNT
                """, new { Day = day.ToDateTime(TimeOnly.MinValue), Rate = rate });
        }

        var partner = await NewPartner();
        var customer = await Register(Sale(partner, 1000m, new DateOnly(2024, 1, 10), new DateOnly(2024, 2, 10)) with
        {
            CurrencyCode = "EUR", ExchangeRate = 4.95m, TotalAmountRon = 4950m, CompanyId = company,
        });
        var supplier = await Register(Purchase(partner, 2000m, new DateOnly(2024, 1, 15)) with
        {
            CurrencyCode = "EUR", ExchangeRate = 4.95m, TotalAmountRon = 9900m, CompanyId = company,
        });
        var advance = await Register(Sale(partner, 500m, new DateOnly(2024, 1, 20), new DateOnly(2024, 1, 20)) with
        {
            CurrencyCode = "EUR", ExchangeRate = 4.95m, TotalAmountRon = 2475m, IsAdvanceInvoice = true, CompanyId = company,
        });

        // Incasarea din 10 februarie (1.000 EUR la 4,99), inregistrata inainte de reevaluarea lui ianuarie: castig realizat 40 fata de 4.950.
        await InTransaction(sp => sp.GetRequiredService<ISettlementService>().RegisterPaymentAsync(new PaymentSettlementRequest(company, partner,
            PartnerRole.Customer, "Finance.CashDocument", Interlocked.Increment(ref paymentSequence), "", "FX-IN", new DateOnly(2024, 2, 10), "EUR", 4.99m, 1000m,
            4990m, false, [new SettlementTarget(customer, null)], false), Ct), company);
        Assert.Equal(40m, await Query<decimal>("SELECT SUM(DifferenceRon) FROM fin.FxDifference WHERE OpenItemId = @Id", new { Id = customer }));

        // A7: rularea lui ianuarie, generata dupa plata, foloseste restul de la sfarsitul lunii; A8: avansul nu intra.
        var january = await SendTo(company, new GenerateFxRevaluation(2024, 1));
        var draft = await SendTo(company, new GetFxRevaluation(january));
        Assert.Equal([(customer, "1000.00", "4950.00", "4970.00", "20.00"), (supplier, "-2000.00", "-9900.00", "-9940.00", "-40.00")],
            draft.Lines.OrderBy(l => l.OpenItemId).Select(l => (l.OpenItemId!.Value, l.ForeignAmount, l.CarryingAmountRon, l.RevaluedAmountRon, l.DifferenceRon)));
        Assert.DoesNotContain(draft.Lines, l => l.OpenItemId == advance);

        // Validarea: datoria devine −9.940 la 4,97; partea facturii platite in februarie corecteaza diferenta realizata (40 − 20), total 40 = 4.990 − 4.950.
        var validated = await SendTo(company, new ValidateFxRevaluation(january, draft.RowVersion));
        Assert.Equal((-9940m, 4.97m), await Query<(decimal, decimal)>("SELECT OpenAmountRon, CarryingRate FROM fin.OpenItem WHERE Id = @Id", new { Id = supplier }));
        Assert.Equal(0m, await Query<decimal>("SELECT OpenAmountRon FROM fin.OpenItem WHERE Id = @Id", new { Id = customer }));
        Assert.Equal(20m, await Query<decimal>("SELECT SUM(DifferenceRon) FROM fin.FxDifference WHERE OpenItemId = @Id", new { Id = customer }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%ReevaluareValutaraValidata' AND Payload LIKE @Like",
            new { Like = $"%\"runId\":{january}[,}}]%" }));

        // A9: a doua rulare validata pe aceeasi luna e refuzata; stornarea readuce exact valorile de dinainte.
        Assert.Equal("FIN_FX_RUN_ALREADY_VALIDATED", (await Assert.ThrowsAsync<ErpException>(() => SendTo(company, new GenerateFxRevaluation(2024, 1)))).Code);
        await SendTo(company, new ReverseFxRevaluation(january, validated.RowVersion));
        Assert.Equal((-9900m, 4.95m), await Query<(decimal, decimal)>("SELECT OpenAmountRon, CarryingRate FROM fin.OpenItem WHERE Id = @Id", new { Id = supplier }));
        Assert.Equal(40m, await Query<decimal>("SELECT SUM(DifferenceRon) FROM fin.FxDifference WHERE OpenItemId = @Id", new { Id = customer }));
        Assert.Equal(FxRunStatus.Reversed, (await SendTo(company, new GetFxRevaluation(january))).Status);

        // Dupa stornare se genereaza si se valideaza alta rulare; cu Fin.Fx.RevalueAdvances pornita, avansul intra in februarie.
        var again = await SendTo(company, new GenerateFxRevaluation(2024, 1));
        await SendTo(company, new ValidateFxRevaluation(again, (await SendTo(company, new GetFxRevaluation(again))).RowVersion));
        await InTransaction(sp => sp.GetRequiredService<Application.Bank.IFinanceSettings>().SaveAsync(company, "Fin.Fx.RevalueAdvances", "true", Database.AdminId, Ct)
            .ContinueWith(_ => 0, TaskScheduler.Default), company);
        var february = await SendTo(company, new GetFxRevaluation(await SendTo(company, new GenerateFxRevaluation(2024, 2))));
        Assert.Equal([(supplier, "-20.00"), (advance, "15.00")], february.Lines.OrderBy(l => l.OpenItemId).Select(l => (l.OpenItemId!.Value, l.DifferenceRon)));
        await SendTo(company, new DeleteFxRevaluationDraft(february.Id, february.RowVersion));
    }

    private Task<T> SendTo<T>(int company, MediatR.IRequest<T> request) => Send(request, companyId: company);

    private async Task<(int Id, string Iban)> NewBankAccount()
    {
        var iban = Bank.StatementFiles.RomanianIban("RNCB", $"9{Guid.NewGuid().ToString("N")[..15].ToUpperInvariant()}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var bankAccountId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.CompanyBankAccount (CompanyId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
            VALUES (@CompanyId, @Iban, N'BCR', 'RON', 0, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { CompanyId = Database.CompanyId, Iban = iban });
        var saved = await Send(new SaveTreasuryAccount(null, Contracts.Treasury.TreasuryAccountKind.Bank, Unique("BANCA-"), "Cont efecte", null, "RON", bankAccountId,
            null, null, null, null, null, null, Erp.Finance.Domain.Treasury.CashCloseMode.OnDemand, null, true, [], null));
        return (saved.Id, iban);
    }

    private async Task<IEnumerable<T>> QueryAll<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.QueryAsync<T>(sql, parameters);
    }
}
