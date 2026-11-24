using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Accounting;
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

        var partner = await Setup();
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);
        Assert.True((await Post(PostingAdapters.SalesInvoice(Sale(partner, 1190m, 190m)))).Posted);
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
        Assert.EndsWith(".xml", file.FileName, StringComparison.Ordinal);
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

    [Fact]
    [Trait("Criteriu", "02.02-C4-A2")]
    [Trait("Criteriu", "02.02-C4-A7")]
    public async Task The_vat_return_matches_the_vat_settlement_entry_and_differences_from_p300_need_an_explanation()
    {
        var partner = await Setup();
        var march = new DateOnly(2026, 3, 10);
        Assert.True((await Post(PostingAdapters.SalesInvoice(Sale(partner, 12100m, 2100m, march)))).Posted);
        Assert.True((await Post(PostingAdapters.SupplierInvoice(Purchase(partner, 5950m, 950m, march)))).Posted);
        var from = new DateOnly(2026, 3, 1);
        var to = new DateOnly(2026, 3, 31);
        var id = await Send(new CalculateTaxReturn("D300", from, to));
        var d300 = (await Send(new GetTaxReturn(id))).Lines.Where(l => l.RowCode == "TOTAL").ToDictionary(l => l.ColumnCode, l => l.CalculatedValue);
        Assert.Equal(("2100.00", "950.00", "1150.00"), (d300["COLLECTED"], d300["DEDUCTIBLE"], d300["DUE"]));

        // C4-A2: D300 = nota de regularizare a TVA a lunii (4427 debit, 4426 credit, 4423 credit).
        var period = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods.Single(p => p.PeriodNo == 3);
        var vat = (await Send(new PreviewClosingEntries(period.Id))).VatSettlement;
        Assert.Contains(vat, l => l is { AccountCode: "4427", Side: 1 } && l.AmountRon == d300["COLLECTED"]);
        Assert.Contains(vat, l => l is { AccountCode: "4426", Side: 2 } && l.AmountRon == d300["DEDUCTIBLE"]);
        Assert.Contains(vat, l => l is { AccountCode: "4423", Side: 2 } && l.AmountRon == d300["DUE"]);

        // C4-A7: P300 cu baza livrarilor dubla (diferenta 10.000 lei, 50%) → peste prag; TVA-ul livrarilor (2.100 lei) → sub prag.
        var comparison = await Send(new ImportPrecompletedVat(id, [new("R9", "BASE", "20000"), new("R9", "VAT", "0"), new("R24", "BASE", "5000"), new("R24", "VAT", "950")]));
        Assert.True(comparison.Single(c => c is { RowCode: "R9", ColumnCode: "BASE" }).IsOverThreshold);
        Assert.False(comparison.Single(c => c is { RowCode: "R9", ColumnCode: "VAT" }).IsOverThreshold);
        Assert.Equal("0.00", comparison.Single(c => c is { RowCode: "R24", ColumnCode: "VAT" }).Difference);
        var unexplained = await Assert.ThrowsAnyAsync<ErpException>(async () => await Send(new VerifyTaxReturn(id, (await Send(new GetTaxReturn(id))).RowVersion)));
        Assert.Equal("FIN_TAX_RETURN_REASON_REQUIRED", unexplained.Code);
        await Send(new ExplainTaxDifference(id, "R9", "BASE", "Factura FV-2 a fost anulată în e-Factura și reemisă în aprilie."));

        // Recalcularea pastreaza explicatia; decizia 5: diferentele nu blocheaza generarea.
        await Send(new CalculateTaxReturn("D300", from, to));
        var draft = await Send(new GetTaxReturn(id));
        Assert.NotNull(draft.Comparison.Single(c => c is { RowCode: "R9", ColumnCode: "BASE" }).Explanation);
        var version = await Send(new VerifyTaxReturn(id, draft.RowVersion));
        await SetMonth(3, 3);
        await Send(new GenerateTaxReturn(id, version));
        var file = await Send(new DownloadTaxReturnFile((await Send(new GetTaxReturn(id))).Files.Single().Id));
        var xml = System.Xml.Linq.XDocument.Load(new MemoryStream(file.Content)).Root!;
        Assert.Equal(("declaratie300", "3", "L"), (xml.Name.LocalName, (string?)xml.Attribute("luna"), (string?)xml.Attribute("tip_decont")));
        Assert.Equal(("10000", "2100", "950", "1150"), ((string?)xml.Attribute("R9_1"), (string?)xml.Attribute("R9_2"), (string?)xml.Attribute("R24_2"),
            (string?)xml.Attribute("R40_2")));
    }

    [Fact]
    [Trait("Criteriu", "02.02-C4-A4")]
    public async Task An_intra_community_services_purchase_is_collected_and_deducted_in_d300_and_reported_with_code_p_in_d390()
    {
        var local = await Setup();
        var german = await Scalar("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Beratung GmbH', 2, @Fiscal, 0, 1, 1, N'Hauptstraße 1', N'Berlin', 'DE', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("DE-"), Fiscal = Unique("DE") });
        var day = new DateOnly(2026, 4, 14);
        var id = Interlocked.Increment(ref sequence);
        var invoice = Purchase(german, 2000m, 0m, day) with
        {
            Accounting = new AccountingPayload("Purchasing.SupplierInvoice", $"FF-{id}", day, day, "RON", 1m, ["ReverseCharge", "Services"],
                [new AccountingComponent("Total", 2000m, 2000m), new AccountingComponent("Net", 2000m, 2000m), new AccountingComponent("Vat", 420m, 420m)])
            {
                SourceModule = "Purchasing", DocumentId = id, PartnerId = german,
            },
        };
        Assert.True((await Post(PostingAdapters.SupplierInvoice(invoice))).Posted);
        Assert.True((await Post(PostingAdapters.SalesInvoice(Sale(local, 1210m, 210m, day)))).Posted);
        var from = new DateOnly(2026, 4, 1);
        var to = new DateOnly(2026, 4, 30);

        // D300: TVA-ul serviciului e si colectat (rândul 7), si deductibil (rândul 21); de plata ramane doar TVA-ul vanzarii.
        var d300 = await Send(new GetTaxReturn(await Send(new CalculateTaxReturn("D300", from, to))));
        var totals = d300.Lines.Where(l => l.RowCode == "TOTAL").ToDictionary(l => l.ColumnCode, l => l.CalculatedValue);
        Assert.Equal(("630.00", "420.00", "210.00"), (totals["COLLECTED"], totals["DEDUCTIBLE"], totals["DUE"]));
        Assert.Contains(d300.Lines, l => l is { RowCode: "A-IC-S", ColumnCode: "VAT", CalculatedValue: "420.00" });

        // D390: codul P pe partenerul german; D394: doar vanzarea din tara.
        var d390 = await Send(new GetTaxReturn(await Send(new CalculateTaxReturn("D390", from, to))));
        var p = d390.PartnerLines.Single();
        Assert.Equal(("P", "DE", "2000.00", (string?)null), (p.Section, p.PartnerCountryCode, p.BaseRon, p.Issue));
        var d394 = await Send(new GetTaxReturn(await Send(new CalculateTaxReturn("D394", from, to))));
        Assert.Equal(["L"], d394.PartnerLines.Select(l => l.Section));
        Assert.Equal("1000.00", d394.Lines.Single(l => l is { RowCode: "L", ColumnCode: "BASE" }).CalculatedValue);

        // Generarea D390 dupa inchiderea lunii: un element pe partener.
        var version = await Send(new VerifyTaxReturn(d390.Id, d390.RowVersion));
        await SetMonth(4, 3);
        await Send(new GenerateTaxReturn(d390.Id, version));
        var file = await Send(new DownloadTaxReturnFile((await Send(new GetTaxReturn(d390.Id))).Files.Single().Id));
        var xml = System.Xml.Linq.XDocument.Load(new MemoryStream(file.Content)).Root!;
        var op = xml.Elements().Single();
        Assert.Equal(("declaratie390", "P", "2000"), (xml.Name.LocalName, (string?)op.Attribute("tip"), (string?)op.Attribute("baza")));
    }

    [Fact]
    public async Task The_saft_file_is_refused_with_accounts_missing_their_saft_code_and_otherwise_lists_the_ledger_and_invoices()
    {
        var partner = await Setup();
        var day = new DateOnly(2026, 5, 20);
        Assert.True((await Post(PostingAdapters.SalesInvoice(Sale(partner, 2420m, 420m, day)))).Posted);
        Assert.True((await Post(PostingAdapters.SupplierInvoice(Purchase(partner, 1190m, 190m, day)))).Posted);
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 31);
        var id = await Send(new CalculateTaxReturn("D406", from, to));
        var draft = await Send(new GetTaxReturn(id));
        var summary = draft.Lines.ToDictionary(l => l.RowCode, l => l.CalculatedValue);
        Assert.Equal(("2", "2"), (summary["ENTRIES"].Split('.')[0], summary["INVOICES"].Split('.')[0]));
        Assert.Empty(await Send(new GetSaftChecks(from, to)));
        var version = await Send(new VerifyTaxReturn(id, draft.RowVersion));
        await SetMonth(5, 3);

        // 51951: un cont cu rulaj fara cont SAF-T (nici el, nici conturile parinte) opreste fisierul.
        await Single<int>("UPDATE fin.GlAccount SET SaftAccountCode = NULL WHERE Code IN ('7', '70', '707'); SELECT @@ROWCOUNT;", new { });
        try
        {
            Assert.Contains(await Send(new GetSaftChecks(from, to)), i => i is { Category: 1, Reference: "707" });
            var missing = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new GenerateTaxReturn(id, version)));
            Assert.Equal("FIN_SAFT_ACCOUNT_CODE_MISSING", missing.Code);
        }
        finally
        {
            await Single<int>("UPDATE fin.GlAccount SET SaftAccountCode = Code WHERE Code IN ('7', '70', '707'); SELECT @@ROWCOUNT;", new { });
        }

        await Send(new GenerateTaxReturn(id, version));
        var file = await Send(new DownloadTaxReturnFile((await Send(new GetTaxReturn(id))).Files.Single().Id));
        var root = System.Xml.Linq.XDocument.Load(new MemoryStream(file.Content)).Root!;
        System.Xml.Linq.XNamespace ns = "mfp:anaf:dgti:d406:declaratie:v1";
        var ledger = root.Element(ns + "GeneralLedgerEntries")!;
        Assert.Equal(2, ledger.Descendants(ns + "Transaction").Count());
        Assert.Equal((string?)ledger.Element(ns + "TotalDebit"), (string?)ledger.Element(ns + "TotalCredit"));
        var debit = ledger.Descendants(ns + "DebitAmount").Sum(a => decimal.Parse((string)a.Element(ns + "Amount")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(decimal.Parse((string)ledger.Element(ns + "TotalDebit")!, System.Globalization.CultureInfo.InvariantCulture), debit);
        var sales = root.Element(ns + "SourceDocuments")!.Element(ns + "SalesInvoices")!.Elements(ns + "Invoice").Single();
        Assert.Equal("420.00", (string?)sales.Descendants(ns + "TaxAmount").Single().Element(ns + "Amount"));
        var account = root.Descendants(ns + "Account").Single(a => (string?)a.Element(ns + "AccountID") == "4111");
        Assert.Equal("2420.00", (string?)account.Element(ns + "ClosingDebitBalance"));
    }

    private async Task<int> Setup()
    {
        await Send(new CreateFiscalYear(2026));
        var center = await Scalar("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Vânzări', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("TR") });
        await Scalar("""
            IF NOT EXISTS (SELECT 1 FROM fin.CompanySetting WHERE CompanyId = @Company AND Code = @Code)
                INSERT INTO fin.CompanySetting (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, @Code, @Value, 1);
            SELECT 0;
            """,
            new { Company = Database.CompanyId, Code = PostingService.DefaultCostCenterSetting, Value = center.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var partner = await Scalar("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Client D300 SRL', 1, @Fiscal, 1, 1, 1, N'Str. Mică 2', N'Iași', N'IS', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("CD-"), Fiscal = Unique("RO7") });
        return partner;
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

    private FacturaEmisa Sale(int partner, decimal total, decimal vat, DateOnly? on = null)
    {
        var id = Interlocked.Increment(ref sequence);
        var date = on ?? new DateOnly(2026, 10, 12);
        return new FacturaEmisa(id, partner, $"FV-{id}", date, date, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
    }

    private FacturaFurnizorValidata Purchase(int partner, decimal total, decimal vat, DateOnly date)
    {
        var id = Interlocked.Increment(ref sequence);
        return new FacturaFurnizorValidata(id, partner, $"FF-{id}", date, date, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.ReceivedFromSpv, null) { CompanyId = Database.CompanyId };
    }

    private async Task<T> Single<T>(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private Task<int> Scalar(string sql, object parameters) => Single<int>(sql, parameters);
}
