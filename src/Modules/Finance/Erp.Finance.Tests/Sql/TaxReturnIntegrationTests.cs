using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Settlement;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Cadrul declaratiilor (02.02, C4 3.3, 3.7, 4.1): calendarul fiscal cu termenele mutate pe ziua lucratoare, generarea doar pe lunile inchise si
/// cu datele neschimbate, depunerea cu obligatia de plata in prognoza, marcajul „De verificat” la redeschidere.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TaxReturnIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static long sequence = 975_000;

    public TaxReturnIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.TaxView, FinancePermissions.TaxPrepare, FinancePermissions.TaxGenerate, FinancePermissions.TaxSubmit,
            FinancePermissions.PeriodView, FinancePermissions.FiscalYearClose]);

    [Fact]
    [Trait("Criteriu", "02.02-C4-A5")]
    [Trait("Criteriu", "02.02-C4-A10")]
    [Trait("Criteriu", "02.02-C4-A11")]
    public async Task A_vat_return_is_generated_only_on_closed_unchanged_months_and_its_payment_goes_to_the_forecast()
    {
        await Send(new CreateFiscalYear(2026));
        Assert.True(await Send(new GenerateTaxCalendar(2026)) > 0);
        var calendar = await Send(new GetTaxCalendar(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30)));
        // C4-A10: D300 pe septembrie are termenul 25 octombrie 2026, duminica, deci luni 26; octombrie ramane pe 25 noiembrie.
        Assert.Equal(new DateOnly(2026, 10, 26), calendar.Single(c => c.FormCode == "D300" && c.PeriodFrom == new DateOnly(2026, 9, 1)).DueDate);
        Assert.Equal(new DateOnly(2026, 11, 25), calendar.Single(c => c.FormCode == "D300" && c.PeriodFrom == new DateOnly(2026, 10, 1)).DueDate);

        var center = await Scalar("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Vânzări', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("TR") });
        await Scalar("INSERT INTO fin.CompanySetting (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, @Code, @Value, 1); SELECT 0;",
            new { Company = Database.CompanyId, Code = PostingService.DefaultCostCenterSetting, Value = center.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var partner = await Scalar("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Client D300 SRL', 1, N'RO776655', 1, 1, 1, N'Str. Mică 2', N'Iași', N'IS', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("CD-") });
        Assert.True((await Post(PostingAdapters.SalesInvoice(Sale(partner, 1190m, 190m)))).Posted);

        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);
        var id = await Send(new CalculateTaxReturn("D300", from, to));
        var draft = await Send(new GetTaxReturn(id));
        Assert.Equal(("190.00", (byte)1, new DateOnly(2026, 11, 25)), (draft.AmountDue, draft.Status, draft.DueDate!.Value));
        var version = await Send(new VerifyTaxReturn(id, draft.RowVersion));

        // C4-A5: luna deschisa → 51943.
        var open = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new GenerateTaxReturn(id, version)));
        Assert.Equal("FIN_TAX_RETURN_PERIOD_OPEN", open.Code);

        // Datele s-au schimbat de la calcul (o factura noua in luna redeschisa) → 51944.
        await SetMonth(10, 1);
        Assert.True((await Post(PostingAdapters.SalesInvoice(Sale(partner, 119m, 19m)))).Posted);
        await SetMonth(10, 3);
        var changed = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new GenerateTaxReturn(id, version)));
        Assert.Equal("FIN_TAX_RETURN_DATA_CHANGED", changed.Code);

        // Recalculata, verificata si generata; depusa cu numarul de inregistrare.
        Assert.Equal(id, await Send(new CalculateTaxReturn("D300", from, to)));
        var recalculated = await Send(new GetTaxReturn(id));
        Assert.Equal("209.00", recalculated.AmountDue);
        version = await Send(new VerifyTaxReturn(id, recalculated.RowVersion));
        version = await Send(new GenerateTaxReturn(id, version));
        var generated = await Send(new GetTaxReturn(id));
        Assert.Equal((byte)3, generated.Status);
        var file = await Send(new DownloadTaxReturnFile(generated.Files.Single().Id));
        Assert.Contains("D300", file.FileName, StringComparison.Ordinal);
        await Send(new SubmitTaxReturn(id, version, "INTERNT-123456"));

        // C4-A10: obligatia de plata si suma planificata in prognoza F5 la scadenta.
        var obligation = await Single<(decimal Amount, DateTime DueDate, int? PlanItemId)>(
            "SELECT Amount, DueDate, PlanItemId FROM fin.TaxObligation WHERE ReturnId = @Id", new { Id = id });
        Assert.Equal((209m, new DateTime(2026, 11, 25)), (obligation.Amount, obligation.DueDate));
        Assert.Equal(209m, await Single<decimal>("SELECT Amount FROM fin.CashFlowPlanItem WHERE Id = @Id", new { Id = obligation.PlanItemId }));
        var submitted = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new CalculateTaxReturn("D300", from, to)));
        Assert.Equal("FIN_TAX_RETURN_ALREADY_SUBMITTED", submitted.Code);

        // C4-A11: redeschiderea lunii marcheaza declaratia; reinchiderea fara schimbari sterge marcajul, cu schimbari il pastreaza.
        await Recheck(from, to, reopened: true);
        Assert.True((await Send(new GetTaxReturn(id))).NeedsReview);
        await Recheck(from, to, reopened: false);
        Assert.False((await Send(new GetTaxReturn(id))).NeedsReview);
        await Recheck(from, to, reopened: true);
        await SetMonth(10, 1);
        Assert.True((await Post(PostingAdapters.SalesInvoice(Sale(partner, 238m, 38m)))).Posted);
        await SetMonth(10, 3);
        await Recheck(from, to, reopened: false);
        Assert.True((await Send(new GetTaxReturn(id))).NeedsReview);
    }

    private Task<PostingOutcome> Post(PostingRequest request) => InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(request, Ct));

    private Task<bool> Recheck(DateOnly from, DateOnly to, bool reopened) => InTransaction(async sp =>
    {
        await sp.GetRequiredService<TaxReturnService>().RecheckAsync(Database.CompanyId, from, to, reopened, Ct);
        return true;
    });

    private async Task SetMonth(int month, byte status)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE fin.AccountingPeriod SET Status = @Status WHERE CompanyId = @Company AND StartDate = @Start",
            new { Company = Database.CompanyId, Status = status, Start = new DateTime(2026, month, 1) });
    }

    private FacturaEmisa Sale(int partner, decimal total, decimal vat)
    {
        var id = Interlocked.Increment(ref sequence);
        var date = new DateOnly(2026, 10, 12);
        return new FacturaEmisa(id, partner, $"FV-{id}", date, date, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
    }

    private async Task<T> Single<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private Task<int> Scalar(string sql, object parameters) => Single<int>(sql, parameters);
}
