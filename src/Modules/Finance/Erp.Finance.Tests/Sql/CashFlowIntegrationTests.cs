using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Planning;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Domain.Planning;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>Prognoza de cash-flow (02.02, F5 3.4, 3.5, 4.4): soldul scriptic, incasarile pe istoricul clientului, platile, sumele planificate.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CashFlowIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    private static long sequence = DateTime.UtcNow.Ticks % 1_000_000_000;

    public CashFlowIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.CashFlowView, FinancePermissions.CashFlowPlan, FinancePermissions.CashFlowSnapshot, FinancePermissions.BankView, FinancePermissions.BankStatementImport,
            FinancePermissions.PaymentOrderCreate, FinancePermissions.PaymentOrderValidate]);
    }

    private int Company => Database.CashFlowCompanyId;

    private Task<T> Run<T>(MediatR.IRequest<T> request) => Send(request, companyId: Company);

    [Fact]
    [Trait("Criteriu", "02.02-F5-A9")]
    public async Task The_forecast_starts_from_the_book_balance_spreads_collections_on_history_and_counts_unconfirmed_orders_once()
    {
        var today = BusinessDay.Today(TimeProvider.System);
        var (account, iban) = await NewBank("RON", Company);
        await Import(account, StatementFiles.Camt053(iban, "RON", 100_000m, [new TestEntry(today.AddDays(-1), 0.01m, Unique("SOLD"))], "1"), companyId: Company);
        var statementBalance = await Query<decimal>("SELECT TOP (1) ClosingBalance FROM fin.BankStatement WHERE TreasuryAccountId = @Id ORDER BY Id DESC",
            new { Id = account });

        // Un OP validat neconfirmat stinge factura si scade din soldul scriptic; nu mai apare nici ca plata asteptata (A9).
        var (supplier, supplierBank) = await Supplier();
        var paid = await SupplierInvoice(supplier, 10_000m, today.AddDays(-2));
        var order = await Order(account, supplier, supplierBank, "10000", paid, today);
        await Run(new ValidatePaymentOrder(order.Id, order.RowVersion));
        var dueLater = await SupplierInvoice(supplier, 3_000m, today.AddDays(5));
        var inDraft = await SupplierInvoice(supplier, 2_000m, today.AddDays(1));
        await Order(account, supplier, supplierBank, "2000", inDraft, today.AddDays(3));

        // Istoricul clientului: 3 facturi la termen, 2 cu 20 de zile intarziere → 60% la termen, 40% in 1–30 de zile (in medie 20).
        var customer = await Customer();
        for (var i = 0; i < 5; i++)
        {
            var due = today.AddDays(-60);
            var invoice = await CustomerInvoice(customer, 1_000m, due);
            await CustomerPayment(customer, invoice, 1_000m, i < 3 ? due : due.AddDays(20));
        }

        Assert.Equal(1, await Run(new RecalculatePaymentBehavior()));
        await CustomerInvoice(customer, 10_000m, today.AddDays(10));

        var categories = await Run(new GetCashFlowCategories());
        var salaries = categories.Single(c => c.Code == "SALARII");
        await Run(new SaveCashFlowPlanItem(null, salaries.Id, "Salarii nete", "RON", "5000", account, today.AddDays(-40), Recurrence.Monthly, 10, null));
        var expectedSalaries = CashFlowRules.Occurrences(today.AddDays(-40), Recurrence.Monthly, 10, null, today, today.AddDays(40)).Count * -5_000m;

        var forecast = await Run(new GetCashFlowForecast(today, today.AddDays(40), Granularity.Day, Prudent: true));
        Assert.Equal(Money(statementBalance - 10_000m), forecast.OpeningBalanceRon);
        Assert.Equal(Money(-5_000m), Row(forecast.Base, "PLATI-FURNIZORI").TotalRon);
        var customers = Row(forecast.Base, "INC-CLIENTI");
        Assert.Equal(Money(10_000m), customers.TotalRon);
        Assert.Equal(Money(6_000m), customers.AmountsRon[10]);
        Assert.Equal(Money(4_000m), customers.AmountsRon[30]);
        Assert.Equal(Money(expectedSalaries), Row(forecast.Base, "SALARII").TotalRon);
        Assert.DoesNotContain(salaries.Name, forecast.EmptyCategories);
        Assert.Equal(Money(statementBalance - 10_000m - 5_000m + 10_000m + expectedSalaries), forecast.Base.Periods[^1].ClosingRon);
        Assert.Equal(Money(6_000m), Row(forecast.Prudent!, "INC-CLIENTI").TotalRon);

        var tooLong = await Assert.ThrowsAsync<ErpException>(() => Run(new GetCashFlowForecast(today, today.AddDays(100), Granularity.Day)));
        Assert.Equal("FIN_CASHFLOW_HORIZON_TOO_LONG", tooLong.Code);
        _ = dueLater;
    }

    [Fact]
    public async Task Delay_buckets_must_cover_the_axis_and_saving_them_recalculates_the_history()
    {
        var defaults = await Run(new GetPaymentDelayBuckets());
        Assert.Equal(["B0", "B1", "B2", "B3", "B4", "B5"], defaults.Select(b => b.Code));
        await Assert.ThrowsAnyAsync<ErpException>(() => Run(new SavePaymentDelayBuckets([new("A", "A", null, 10, false), new("B", "B", 12, null, false)])));
        var saved = await Run(new SavePaymentDelayBuckets([new("T", "La termen", null, 0, false), new("I", "Întârziat", 1, 60, false),
            new("X", "Incert", 61, null, true)]));
        Assert.Equal(["T", "I", "X"], saved.Select(b => b.Code));
        await Run(new SavePaymentDelayBuckets(defaults));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F5-A12")]
    public async Task An_alert_is_sent_once_a_day_and_a_snapshot_is_compared_with_actuals_by_week()
    {
        var today = BusinessDay.Today(TimeProvider.System);
        var (account, iban) = await NewBank("RON", Company);
        await Import(account, StatementFiles.Camt053(iban, "RON", 1_000m, [new TestEntry(today.AddDays(-1), 0.01m, Unique("SOLD"))], "1"), companyId: Company);
        var rent = (await Run(new GetCashFlowCategories())).Single(c => c.Code == "CHIRII-UTILITATI");
        await Run(new SaveCashFlowPlanItem(null, rent.Id, "Chirie depozit", "RON", "5000", account, today.AddDays(3), Recurrence.Once, null, null));

        var rules = await Run(new SaveCashFlowAlertRules([new CashFlowAlertRuleInput(null, account, "0", 14, null, true)]));
        var rule = Assert.Single(rules);
        Assert.Equal(1, await Run(new CheckCashFlowAlerts()));
        Assert.Equal(0, await Run(new CheckCashFlowAlerts()));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%AlertaCashFlow' AND Payload LIKE '%CHIRII-UTILITATI%'"));
        Assert.Equal(today, (await Run(new GetCashFlowAlertRules())).Single(r => r.Id == rule.Id).LastAlertedOn);

        var id = await Run(new TakeCashFlowSnapshot("Test", today, today.AddDays(27), Granularity.Week, account));
        Assert.Contains(await Run(new GetCashFlowSnapshots()), s => s.Id == id && !s.IsAutomatic);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.CashFlowSnapshotLine WHERE SnapshotId = @Id AND SourceKind = 1", new { Id = id }));
        var comparison = await Run(new GetCashFlowForecastVsActual(id));
        var row = comparison.Rows.Single(r => r.Code == "CHIRII-UTILITATI");
        var week = comparison.Periods.Select((p, i) => (p, i)).Single(x => x.p.Start <= today.AddDays(3) && x.p.End >= today.AddDays(3)).i;
        Assert.Equal(Money(-5_000m), row.ForecastRon[week]);
        Assert.True(comparison.Periods[0].HasActuals);
        Assert.Null(row.ActualRon[^1]);
        await Run(new SaveCashFlowAlertRules([]));
    }

    private static string Money(decimal value) => Erp.Finance.Domain.Money.Format(value);

    private static CashFlowRowDto Row(CashFlowScenarioDto scenario, string code) => scenario.Rows.Single(r => r.Code == code);

    private async Task<FinanceSaved> Order(int account, int partner, int partnerBank, string amount, long invoice, DateOnly date) =>
        await Run(new SavePaymentOrder(null, account, date, date, false, PaymentPurpose.Suppliers, partner, null, partnerBank, null, null, null, null, null, null,
            [new PaymentOrderLineInput(await OperationType("PLATA-FURNIZOR"), amount, TreasurySettlement.Reference([invoice]), null, null)], null));

    private async Task<(int Partner, int BankAccount)> Supplier()
    {
        var iban = StatementFiles.RomanianIban("BTRL", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var partner = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Furnizor Prognoză SRL', 1, @FiscalCode, 0, 1, 1, N'Str. 1', N'Brașov', N'BV', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("FCF-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var bank = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.PartnerBankAccount (PartnerId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy) VALUES (@Partner, @Iban, N'BT', 'RON', 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Partner = partner, Iban = iban });
        return (partner, bank);
    }

    private async Task<int> Customer()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Client Prognoză SRL', 1, @FiscalCode, 1, 0, 1, N'Str. 2', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("CCF-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private Task<long> SupplierInvoice(int partner, decimal total, DateOnly due) =>
        InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(new FacturaFurnizorValidata(Interlocked.Increment(ref sequence), partner,
            $"FF-{sequence}", due.AddDays(-30), due.AddDays(-30), "RON", 1m, total, total, 0, 0, false, false, null, false, [new InvoiceDueDate(due, total)], null,
            null, EInvoiceStatus.ReceivedFromSpv, null) { CompanyId = Company }, Ct), Company);

    private Task<long> CustomerInvoice(int partner, decimal total, DateOnly due) =>
        InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(new FacturaEmisa(Interlocked.Increment(ref sequence), partner,
            $"FV-{sequence}", due.AddDays(-30), due.AddDays(-30), "RON", 1m, total, total, 0, 0, false, false, null, false, [new InvoiceDueDate(due, total)], null,
            null, EInvoiceStatus.Accepted, null) { CompanyId = Company }, Ct), Company);

    private Task CustomerPayment(int partner, long invoice, decimal amount, DateOnly date) =>
        InTransaction(sp => sp.GetRequiredService<ISettlementService>().RegisterPaymentAsync(new PaymentSettlementRequest(Company, partner, PartnerRole.Customer,
            "Finance.CashDocument", Interlocked.Increment(ref sequence), "", "INC-CF", date, "RON", 1m, amount, amount, false, [new SettlementTarget(invoice, null)],
            false), Ct), Company);
}
