using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Accounting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Contarea automata (02.02, C2 4.1): factura emisa devine nota validata pe modelul livrat, o singura data pe eveniment; evenimentele care nu
/// pot fi contate (cont negasit, dimensiune lipsa, luna inchisa) intra in coada „De contat” cu motivul, fara sa piarda tranzactia consumatorului.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostingIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day = new(2026, 11, 13);
    private static long sequence = 700_000;

    public PostingIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.JournalEntryView, FinancePermissions.PeriodView, FinancePermissions.PeriodClose,
            FinancePermissions.FiscalYearClose]);

    [Fact]
    [Trait("Criteriu", "02.02-C2-A1")]
    [Trait("Criteriu", "02.02-C2-A4")]
    [Trait("Criteriu", "02.02-C2-A6")]
    public async Task Events_become_validated_entries_once_or_wait_in_the_queue_with_the_reason()
    {
        var partner = await NewPartner();

        // A6: factura de furnizor fara centru de cost pe 628 (clasa 6 il cere) intra in coada cu motivul „dimensiune lipsa”; tranzactia
        // consumatorului ramane valida, iar randul din coada e scris.
        var purchase = Purchase(partner, 238m, 38m);
        var queued = await Post(PostingAdapters.SupplierInvoice(purchase));
        Assert.Equal(PostingFailureReason.DimensionMissing, queued.Reason);
        Assert.Equal((byte)3, await Query<byte>("SELECT Reason FROM fin.PostingQueue WHERE EventId = @Id", new { Id = purchase.EventId }));

        // A6: plata dintr-un cont de trezorerie fara cont contabil: „cont negasit”, cu lantul incercat in detalii.
        var payment = new PlataInregistrata(987_654, TreasuryAccountKind.Bank, "Finance.CashDocument", 1, "DP 1", Day, partner, null, "RON", 1m, 50m, 50m,
            [new TreasuryMovementLine(1, "PLATA_FURNIZOR", 50m, 50m, null, null)]) { CompanyId = Database.CompanyId };
        var missing = await Post(PostingAdapters.Payment(payment));
        Assert.Equal(PostingFailureReason.AccountNotFound, missing.Reason);
        Assert.Contains("nu a dat niciun cont (încercat:", missing.Details, StringComparison.Ordinal);

        // A1: cu centrul de cost implicit al firmei, factura emisa standard: 4111 = 707 + 4427, partenerul pe 4111, centrul pe 707.
        var center = await CostCenter();
        await Setting(PostingService.DefaultCostCenterSetting, center.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var sale = Sale(partner, 1190m, 190m, Day);
        var posted = await Post(PostingAdapters.SalesInvoice(sale));
        Assert.True(posted.Posted);
        var lines = await Lines(posted.EntryId!.Value);
        Assert.Equal([("4111", (byte)1, 1190m, (int?)partner, (int?)null), ("707", (byte)2, 1000m, null, center), ("4427", (byte)2, 190m, null, null)], lines);
        Assert.Equal("VZ", await Query<string>("""
            SELECT j.Code FROM fin.JournalEntry AS e JOIN fin.Journal AS j ON j.Id = e.JournalId WHERE e.Id = @Id AND e.Status = 2 AND e.Number IS NOT NULL
            """, new { Id = posted.EntryId }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%NotaContabilaValidata' AND Payload LIKE @P",
            new { P = $"%\"entryId\":{posted.EntryId},%" }));

        // A4: acelasi eveniment livrat din nou nu dubleaza nota.
        var again = await Post(PostingAdapters.SalesInvoice(sale));
        Assert.Equal(posted.EntryId, again.EntryId);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.JournalEntry WHERE SourceEventId = @Id", new { Id = sale.EventId }));

        // Factura cu TVA la incasare: TVA pe 4428, nu pe 4427.
        var onCollection = await Post(PostingAdapters.SalesInvoice(Sale(partner, 119m, 19m, Day) with { IsVatOnCollection = true }));
        Assert.Contains(("4428", (byte)2, 19m, (int?)null, (int?)null), await Lines(onCollection.EntryId!.Value));

        // Reincercarea facturii de furnizor (acum cu centru implicit) rezolva randul din coada.
        var retried = await Post(PostingAdapters.SupplierInvoice(purchase));
        Assert.True(retried.Posted);
        Assert.Equal((byte)2, await Query<byte>("SELECT Status FROM fin.PostingQueue WHERE EventId = @Id", new { Id = purchase.EventId }));

        // Luna inchisa: evenimentul asteapta in coada (motivul 4), nu se pierde.
        await Send(new CreateFiscalYear(2026));
        var january = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods[0];
        await Send(new ClosePeriod(january.Id, january.RowVersion));
        var late = await Post(PostingAdapters.SalesInvoice(Sale(partner, 100m, 0m, new DateOnly(2026, 1, 15))));
        Assert.Equal(PostingFailureReason.ClosedPeriod, late.Reason);
    }

    private Task<PostingOutcome> Post(PostingRequest request) => InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(request, Ct));

    private async Task<List<(string, byte, decimal, int?, int?)>> Lines(long entryId)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await connection.QueryAsync<(string, byte, decimal, int?, int?)>("""
            SELECT a.Code, l.Side, l.AmountRon, l.PartnerId, l.CostCenterId
            FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId WHERE l.EntryId = @Id ORDER BY l.LineNumber
            """, new { Id = entryId })).ToList();
    }

    private FacturaEmisa Sale(int partner, decimal total, decimal vat, DateOnly date)
    {
        var id = Interlocked.Increment(ref sequence);
        return new FacturaEmisa(id, partner, $"FV-{id}", date, date, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
    }

    private FacturaFurnizorValidata Purchase(int partner, decimal total, decimal vat)
    {
        var id = Interlocked.Increment(ref sequence);
        return new FacturaFurnizorValidata(id, partner, $"FF-{id}", Day, Day, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(Day.AddDays(30), total)], null, null, EInvoiceStatus.ReceivedFromSpv, null) { CompanyId = Database.CompanyId };
    }

    private async Task Setting(string code, string value)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync("""
            MERGE fin.CompanySetting AS t USING (SELECT @Company AS CompanyId, @Code AS Code) AS s ON t.CompanyId = s.CompanyId AND t.Code = s.Code
            WHEN MATCHED THEN UPDATE SET Value = @Value
            WHEN NOT MATCHED THEN INSERT (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, @Code, @Value, 1);
            """, new { Company = Database.CompanyId, Code = code, Value = value });
    }

    private async Task<int> NewPartner()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Partener Contare SRL', 1, @FiscalCode, 1, 1, 1, N'Str. Mare 1', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("PC-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Vânzări', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("VZ") });
    }
}
