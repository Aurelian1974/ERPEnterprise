using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Contracts.Settlement;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Registrul de TVA (02.02, C4 3.1): scris odata cu nota, egal cu rulajele 4427 si 4426 ale lunii; TVA la incasare doar in luna incasarii, pentru
/// partea incasata; stornarea in rosu, in luna ei.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class VatRegisterIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static long sequence = 970_000;

    public VatRegisterIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.TaxView, FinancePermissions.JournalEntryView]);

    [Fact]
    [Trait("Criteriu", "02.02-C4-A1")]
    [Trait("Criteriu", "02.02-C4-A3")]
    public async Task The_register_matches_4427_and_4426_and_vat_on_collection_is_declared_when_collected()
    {
        var center = await Scalar("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Vânzări', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("TV") });
        await Scalar("INSERT INTO fin.CompanySetting (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, @Code, @Value, 1); SELECT 0;",
            new { Company = Database.CompanyId, Code = PostingService.DefaultCostCenterSetting, Value = center.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var partner = await Scalar("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Client TVA SRL', 1, N'RO998877', 1, 1, 1, N'Str. Mare 1', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("CT-") });
        var october = new DateOnly(2026, 10, 10);

        var sale = Sale(partner, 1210m, 210m, october);
        Assert.True((await Post(PostingAdapters.SalesInvoice(sale))).Posted);
        Assert.True((await Post(PostingAdapters.SupplierInvoice(Purchase(partner, 595m, 95m, october)))).Posted);
        var onCollection = Sale(partner, 1190m, 190m, october) with { IsVatOnCollection = true };
        Assert.True((await Post(PostingAdapters.SalesInvoice(onCollection))).Posted);
        var storno = Sale(partner, -121m, -21m, new DateOnly(2026, 10, 20)) with { IsReversal = true, ReversedInvoiceId = sale.InvoiceId };
        Assert.True((await Post(PostingAdapters.SalesInvoice(storno))).Posted);

        // Octombrie: vanzarile (cu stornarea totala a primei facturi in rosu) si cumpararea; factura cu TVA la incasare nu apare.
        var sales = await Send(new GetVatJournal(2026, 10, 1));
        Assert.Equal(["L-S21"], sales.Totals.Select(t => t.OperationTypeCode));
        Assert.Equal(("0.00", "0.00"), (sales.Totals[0].BaseRon, sales.Totals[0].VatRon));
        Assert.DoesNotContain(sales.Rows, r => r.IsVatOnCollection);
        var purchases = await Send(new GetVatJournal(2026, 10, 2));
        Assert.Equal(("A-S21", "500.00", "95.00"), (purchases.Totals.Single().OperationTypeCode, purchases.Totals.Single().BaseRon, purchases.Totals.Single().VatRon));

        // Noiembrie: jumatate din factura cu TVA la incasare e incasata, deci TVA-ul ei devine exigibil doar pentru jumatate.
        var exigible = new TvaLaIncasareExigibil(1, FacturaEmisa.DocumentType, onCollection.InvoiceId, 1, new DateOnly(2026, 11, 5), 500m, 95m, false)
        {
            CompanyId = Database.CompanyId,
        };
        Assert.True((await Post(PostingAdapters.VatOnCollection(exigible))).Posted);
        var november = await Send(new GetVatJournal(2026, 11, 1));
        Assert.Equal(("500.00", "95.00", true), (november.Rows.Single().BaseRon, november.Rows.Single().VatRon, november.Rows.Single().IsVatOnCollection));

        // C4-A1: pe fiecare luna, TVA colectata din registru = rulajul creditor 4427, deductibila = rulajul debitor 4426.
        foreach (var (year, month) in new[] { (2026, 10), (2026, 11) })
        {
            Assert.Equal(await Turnover("4427", 2, year, month), await RegisterVat(1, year, month));
            Assert.Equal(await Turnover("4426", 1, year, month), await RegisterVat(2, year, month));
        }
    }

    private Task<PostingOutcome> Post(PostingRequest request) => InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(request, Ct));

    private async Task<decimal> Turnover(string account, byte side, int year, int month)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<decimal>("""
            SELECT ISNULL(SUM(CASE WHEN @Side = 1 THEN l.DebitRon ELSE l.CreditRon END), 0)
            FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId JOIN fin.GlAccount AS a ON a.Id = l.AccountId
            WHERE e.CompanyId = @Company AND e.Status >= 2 AND e.SourceKind <> 4 AND a.Code LIKE @Code + '%' AND e.PeriodYear = @Year AND e.PeriodMonth = @Month
            """, new { Company = Database.CompanyId, Side = side, Code = account, Year = year, Month = month });
    }

    private async Task<decimal> RegisterVat(byte direction, int year, int month)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT ISNULL(SUM(VatRon), 0) FROM fin.VatEntry WHERE CompanyId = @Company AND Direction = @Direction AND PeriodYear = @Year AND PeriodMonth = @Month",
            new { Company = Database.CompanyId, Direction = direction, Year = year, Month = month });
    }

    private FacturaEmisa Sale(int partner, decimal total, decimal vat, DateOnly date)
    {
        var id = Interlocked.Increment(ref sequence);
        return new FacturaEmisa(id, partner, $"FV-{id}", date, date, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
    }

    private FacturaFurnizorValidata Purchase(int partner, decimal total, decimal vat, DateOnly date)
    {
        var id = Interlocked.Increment(ref sequence);
        return new FacturaFurnizorValidata(id, partner, $"FF-{id}", date, date, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.ReceivedFromSpv, null) { CompanyId = Database.CompanyId };
    }

    private async Task<int> Scalar(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }
}
