using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>Notele contabile manuale (02.02, C2 3.5, 3.8, 4.3, 4.5): ciorna, validarea cu verificarile, numarul, stornarea si perioada inchisa.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class JournalEntryIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day = new(2026, 11, 13);

    public JournalEntryIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.JournalEntryReverse, FinancePermissions.PeriodView, FinancePermissions.PeriodClose,
            FinancePermissions.FiscalYearClose]);

    [Fact]
    [Trait("Criteriu", "02.02-C2-A7")]
    [Trait("Criteriu", "02.02-C2-A8")]
    public async Task A_manual_entry_is_checked_numbered_reversed_in_red_and_refused_in_a_closed_month()
    {
        var accounts = (await Send(new GetGlAccounts())).ToDictionary(a => a.Code, a => a.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var center = await CostCenter();

        // A8: 628 fara centru de cost e refuzat (51845); 401 fara dreptul pe conturile tinute de module, refuzat (51851); dezechilibrul (51843).
        Assert.Equal("FIN_ENTRY_DIMENSION_MISSING", (await Refused(journal, Day, [new(accounts["628"], 1, "100"), new(accounts["462"], 2, "100")])).Code);
        Assert.Equal("FIN_ENTRY_CONTROLLED_ACCOUNT", (await Refused(journal, Day, [new(accounts["401"], 1, "100"), new(accounts["462"], 2, "100")])).Code);
        Assert.Equal("FIN_ENTRY_UNBALANCED", (await Refused(journal, Day, [new(accounts["628"], 1, "100", CostCenterId: center),
            new(accounts["462"], 2, "90")])).Code);

        var draft = await Send(new SaveManualEntry(null, journal, Day, "Servicii de consultanță", "FF 991",
            [new(accounts["628"], 1, "1200.50", CostCenterId: center), new(accounts["462"], 2, "1200.50")]));
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
        var entry = await Send(new GetJournalEntry(draft.Id));
        Assert.Equal((2, "DV 00001", "1200.50"), ((int)entry.Status, entry.DisplayNumber, entry.TotalDebit));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%NotaContabilaValidata' AND Payload LIKE @P",
            new { P = $"%\"entryId\":{draft.Id},%" }));
        await Assert.ThrowsAnyAsync<ErpException>(() => Send(new DeleteManualEntry(draft.Id)));

        // Stornarea „in rosu”: aceleasi conturi si parti, sume negative; nota initiala devine stornata.
        var reversal = await Send(new ReverseJournalEntry(draft.Id, Day, "Factură greșită"));
        var red = await Send(new GetJournalEntry(reversal.Id));
        Assert.Equal([(accounts["628"], (byte)1, "-1200.50"), (accounts["462"], (byte)2, "-1200.50")], red.Lines.Select(l => (l.AccountId, l.Side, l.AmountRon)));
        Assert.Equal((3, draft.Id, "DV 00002"), ((int)(await Send(new GetJournalEntry(draft.Id))).Status, red.ReversalOfId!.Value, red.DisplayNumber));

        // In luna inchisa nu se valideaza nicio nota (51848).
        await Send(new CreateFiscalYear(2026));
        var january = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods[0];
        await Send(new ClosePeriod(january.Id, january.RowVersion));
        var closed = await Send(new SaveManualEntry(null, journal, new DateOnly(2026, 1, 20), null, null,
            [new(accounts["628"], 1, "10", CostCenterId: center), new(accounts["462"], 2, "10")]));
        var refused = await Assert.ThrowsAsync<ErpException>(async () =>
            await Send(new ValidateManualEntry(closed.Id, (await Send(new GetJournalEntry(closed.Id))).RowVersion)));
        Assert.Equal("FIN_PERIOD_CLOSED", refused.Code);

        // A7: nicio nota validata nu are total debit diferit de total credit, nici pe antet, nici pe linii.
        Assert.Equal(0, await Query<int>("""
            SELECT COUNT(*) FROM fin.JournalEntry AS e
            WHERE e.Status >= 2 AND (e.TotalDebit <> e.TotalCredit
                OR (SELECT SUM(DebitRon) - SUM(CreditRon) FROM fin.JournalLine WHERE EntryId = e.Id) <> 0)
            """));
    }

    private async Task<ErpException> Refused(int journal, DateOnly day, JournalLineInput[] lines)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, null, null, lines));
        var version = (await Send(new GetJournalEntry(draft.Id))).RowVersion;
        var error = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new ValidateManualEntry(draft.Id, version)));
        await Send(new DeleteManualEntry(draft.Id));
        return error;
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Administrativ', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("ADM") });
    }
}
