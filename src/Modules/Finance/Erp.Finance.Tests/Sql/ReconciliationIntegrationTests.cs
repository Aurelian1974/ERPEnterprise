using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Periods;
using Erp.Finance.Contracts.Settlement;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Concordantele (02.02, C3 3.4): soldul contabil al furnizorilor fata de partidele F3 arata diferenta facturii ramase in coada „De contat”,
/// explicata de ea; clientul contat si inregistrat nu are diferenta; pasul RECONCILIATION blocheaza, cu trecere permisa.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReconciliationIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day = new(2026, 11, 12);

    public ReconciliationIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.PeriodView, FinancePermissions.PeriodSoftClose, FinancePermissions.FiscalYearClose,
            FinancePermissions.JournalEntryView]);

    [Fact]
    [Trait("Criteriu", "02.02-C3-A8")]
    public async Task The_supplier_difference_is_explained_by_the_invoice_waiting_in_the_queue()
    {
        var supplier = await NewPartner("Office Line SRL");
        var customer = await NewPartner("Mega Retail SA");

        // Factura de furnizor intra in partide (F3), dar contarea ei asteapta (628 fara centru de cost).
        var purchase = new FacturaFurnizorValidata(960_001, supplier, "FF OL 7802", Day, Day, "RON", 1m, 2108m, 2108m, 0m, 0m, false, false, null, false,
            [new InvoiceDueDate(Day.AddDays(30), 2108m)], null, null, EInvoiceStatus.ReceivedFromSpv, null) { CompanyId = Database.CompanyId };
        await InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(purchase, Ct));
        Assert.False((await InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(PostingAdapters.SupplierInvoice(purchase), Ct))).Posted);

        // Factura clientului e si in partide, si contata (cu centrul de cost implicit): fara diferenta.
        var center = await Scalar("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Vânzări', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("RC") });
        await Scalar("INSERT INTO fin.CompanySetting (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, @Code, @Value, 1); SELECT 0;",
            new { Company = Database.CompanyId, Code = PostingService.DefaultCostCenterSetting, Value = center.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var sale = new FacturaEmisa(960_002, customer, "FV EXD 10241", Day, Day, "RON", 1m, 1190m, 1190m, 190m, 190m, false, false, null, false,
            [new InvoiceDueDate(Day.AddDays(30), 1190m)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
        await InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(sale, Ct));
        Assert.True((await InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(PostingAdapters.SalesInvoice(sale), Ct))).Posted);

        var rows = await Send(new GetReconciliation(new DateOnly(2026, 11, 30)));
        var difference = Assert.Single(rows);
        Assert.Equal((2, supplier, "0.00", "-2108.00", "2108.00"), (difference.Area, difference.KeyId, difference.Ledger, difference.Subledger, difference.Difference));
        Assert.Equal("FF OL 7802", Assert.Single(difference.Explanations).DocumentNo);

        // Pasul RECONCILIATION al lui noiembrie esueaza si permite trecerea cu motiv.
        await Send(new CreateFiscalYear(2026));
        var november = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods.Single(p => p.PeriodNo == 11);
        var step = (await Send(new RunPeriodClose(november.Id))).Steps.Single(s => s.StepCode == "RECONCILIATION");
        Assert.Equal((CloseCheckOutcome.Failed, true, 1), (step.Outcome, step.AllowOverride, step.Issues.Count));
    }

    private async Task<int> NewPartner(string name) => await Scalar("""
        INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
        VALUES (@Code, @Name, 1, @FiscalCode, 1, 1, 1, N'Str. Mare 1', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
        SELECT CAST(SCOPE_IDENTITY() AS int);
        """, new { Code = Unique("RP-"), Name = name, FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });

    private async Task<int> Scalar(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }
}
