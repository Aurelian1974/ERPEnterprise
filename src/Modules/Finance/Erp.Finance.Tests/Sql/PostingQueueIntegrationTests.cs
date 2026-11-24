using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain.Accounting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Stornarea la sursa, coada „De contat” si recontarea (02.02, C2 4.2, 4.4, 4.6): stornarea foloseste conturile notei initiale; randul din coada
/// se reincearca si se ignora; recontarea unei luni deschise rescrie nota cu istoric, iar luna inchisa nu se recontează.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostingQueueIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day = new(2026, 11, 13);
    private static long sequence = 800_000;

    public PostingQueueIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.JournalEntryView, FinancePermissions.PostingRetry, FinancePermissions.PostingIgnore,
            FinancePermissions.PostingRepost, FinancePermissions.PeriodView, FinancePermissions.PeriodClose, FinancePermissions.FiscalYearClose]);

    [Fact]
    [Trait("Criteriu", "02.02-C2-A5")]
    [Trait("Criteriu", "02.02-C2-A6")]
    [Trait("Criteriu", "02.02-C2-A9")]
    public async Task Reversals_use_the_original_accounts_the_queue_retries_and_reposting_keeps_numbers_with_history()
    {
        var partner = await NewPartner();
        var center = await CostCenter();

        // A5: fara centru de cost, factura de furnizor asteapta in coada (ContareEsuata o singura data); reincercarea fara schimbare o lasa acolo.
        var purchase = Purchase(partner, 500m, Day);
        Assert.Equal(PostingFailureReason.DimensionMissing, (await Post(PostingAdapters.SupplierInvoice(purchase))).Reason);
        await Post(PostingAdapters.SupplierInvoice(purchase));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%ContareEsuata' AND Payload LIKE @P",
            new { P = $"%{purchase.EventId}%" }));
        var waiting = Assert.Single(await Send(new GetPostingQueue()), q => q.EventId == purchase.EventId);
        Assert.Equal(new PostingRetryDto(0, 1), await Send(new RetryPostingQueue(waiting.Id)));

        // A5: dupa setarea centrului de cost implicit, reincercarea contează evenimentul si randul iese din coada.
        await Setting(PostingService.DefaultCostCenterSetting, center.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new PostingRetryDto(1, 0), await Send(new RetryPostingQueue(Reason: PostingFailureReason.DimensionMissing)));
        Assert.DoesNotContain(await Send(new GetPostingQueue()), q => q.EventId == purchase.EventId);

        // Un rand ignorat nu mai e in asteptare.
        var orphan = Purchase(partner, 10m, Day);
        await Execute("DELETE FROM fin.CompanySetting WHERE CompanyId = @Company AND Code = @Code",
            new { Company = Database.CompanyId, Code = PostingService.DefaultCostCenterSetting });
        await Post(PostingAdapters.SupplierInvoice(orphan));
        var row = Assert.Single(await Send(new GetPostingQueue()), q => q.EventId == orphan.EventId);
        await Send(new IgnorePostingQueueItem(row.Id, "Factură anulată la furnizor", row.RowVersion));
        Assert.Equal((byte)3, Assert.Single(await Send(new GetPostingQueue(3)), q => q.Id == row.Id).Status);
        await Setting(PostingService.DefaultCostCenterSetting, center.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // A6: factura emisa pe 4111; rolul clientilor trece pe 4118; stornarea facturii foloseste tot 4111 (nota initiala), cu sume negative.
        var sale = Sale(partner, 1190m, 190m, Day);
        var original = (await Post(PostingAdapters.SalesInvoice(sale))).EntryId!.Value;
        await Role("CustomerReceivable", "4118");
        var storno = Sale(partner, -1190m, -190m, Day) with { IsReversal = true, ReversedInvoiceId = sale.InvoiceId };
        var reversal = (await Post(PostingAdapters.SalesInvoice(storno))).EntryId!.Value;
        Assert.Equal([("4111", (byte)1, -1190m), ("707", (byte)2, -1000m), ("4427", (byte)2, -190m)], await Lines(reversal));
        Assert.Equal((3, original), await Query<(int, long)>("SELECT o.Status, r.ReversalOfId FROM fin.JournalEntry AS o, fin.JournalEntry AS r WHERE o.Id = @O AND r.Id = @R",
            new { O = original, R = reversal }));

        // A9: rolul cheltuielilor din achizitii trece pe 6022; recontarea lunii rescrie nota facturii de furnizor pe 6022, cu acelasi numar si istoric.
        var entry = await Query<(long Id, int Number)>("SELECT Id, Number FROM fin.JournalEntry WHERE SourceEventId = @E", new { E = purchase.EventId });
        await Role("PurchaseExpense", "6022");
        var preview = await Send(new PreviewRepost(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), nameof(FacturaFurnizorValidata)));
        var change = Assert.Single(preview, c => c.EntryId == entry.Id);
        Assert.Contains(change.Current, l => l.AccountCode == "628");
        Assert.Contains(change.Proposed, l => l.AccountCode == "6022");
        Assert.True(await Send(new RepostEntries(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Cheltuieli reclasificate", nameof(FacturaFurnizorValidata))) >= 1);
        Assert.Contains(("6022", (byte)1, 500m), await Lines(entry.Id));
        Assert.Equal(entry.Number, await Query<int>("SELECT Number FROM fin.JournalEntry WHERE Id = @Id", new { entry.Id }));
        Assert.Contains("628", await Query<string>("SELECT Lines FROM fin.JournalEntryHistory WHERE EntryId = @Id AND Version = 1", new { entry.Id }),
            StringComparison.Ordinal);
        Assert.Empty(await Send(new PreviewRepost(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), nameof(FacturaFurnizorValidata))));

        // A9: in luna inchisa nu se recontează (nota nu apare in previzualizare, iar procedura refuza cu 51854).
        await Send(new CreateFiscalYear(2026));
        var year = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026);
        var january = year.Periods[0];
        // Factura e si in partide (F3), ca concordanta lunii sa nu blocheze inchiderea.
        var januaryPurchase = Purchase(partner, 70m, new DateOnly(2026, 1, 10));
        await InTransaction(sp => sp.GetRequiredService<Erp.Finance.Application.Settlement.InvoiceOpenItems>().RegisterAsync(januaryPurchase, Ct));
        var old = await Post(PostingAdapters.SupplierInvoice(januaryPurchase));
        await Send(new ClosePeriod(january.Id, january.RowVersion));
        await Role("PurchaseExpense", "628");
        Assert.DoesNotContain(await Send(new PreviewRepost(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))), c => c.EntryId == old.EntryId);
        var version = await Query<byte[]>("SELECT RowVersion FROM fin.JournalEntry WHERE Id = @Id", new { Id = old.EntryId });
        var refused = await Assert.ThrowsAnyAsync<SqlException>(() => InTransaction(sp => sp.GetRequiredService<IPostingStore>().RepostAsync(Database.CompanyId,
            old.EntryId!.Value, version, null, [], "test", 1, Ct)));
        Assert.Equal(51854, refused.Number);
    }

    private Task<PostingOutcome> Post(PostingRequest request) => InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(request, Ct));

    private async Task<List<(string, byte, decimal)>> Lines(long entryId)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await connection.QueryAsync<(string, byte, decimal)>("""
            SELECT a.Code, l.Side, l.AmountRon FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId WHERE l.EntryId = @Id ORDER BY l.LineNumber
            """, new { Id = entryId })).ToList();
    }

    private async Task Execute(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private Task Role(string role, string code) => Execute("""
        DELETE FROM fin.GlAccountRoleAssignment WHERE CompanyId = @Company AND Role = @Role;
        INSERT INTO fin.GlAccountRoleAssignment (CompanyId, Role, AccountId, ValidFrom, CreatedBy)
        SELECT @Company, @Role, a.Id, '20000101', 1 FROM fin.GlAccount AS a JOIN fin.ChartOfAccounts AS c ON c.Id = a.ChartId AND c.Kind = 1 WHERE a.Code = @Code;
        """, new { Company = Database.CompanyId, Role = role, Code = code });

    private Task Setting(string code, string value) => Execute("""
        MERGE fin.CompanySetting AS t USING (SELECT @Company AS CompanyId, @Code AS Code) AS s ON t.CompanyId = s.CompanyId AND t.Code = s.Code
        WHEN MATCHED THEN UPDATE SET Value = @Value
        WHEN NOT MATCHED THEN INSERT (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, @Code, @Value, 1);
        """, new { Company = Database.CompanyId, Code = code, Value = value });

    private FacturaEmisa Sale(int partner, decimal total, decimal vat, DateOnly date)
    {
        var id = Interlocked.Increment(ref sequence);
        return new FacturaEmisa(id, partner, $"FV-{id}", date, date, "RON", 1m, total, total, vat, vat, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
    }

    private FacturaFurnizorValidata Purchase(int partner, decimal total, DateOnly date)
    {
        var id = Interlocked.Increment(ref sequence);
        return new FacturaFurnizorValidata(id, partner, $"FF-{id}", date, date, "RON", 1m, total, total, 0m, 0m, false, false, null, false,
            [new InvoiceDueDate(date.AddDays(30), total)], null, null, EInvoiceStatus.ReceivedFromSpv, null) { CompanyId = Database.CompanyId };
    }

    private async Task<int> NewPartner()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Partener Coadă SRL', 1, @FiscalCode, 1, 1, 1, N'Str. Mare 1', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("PQ-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Producție', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("PR") });
    }
}
