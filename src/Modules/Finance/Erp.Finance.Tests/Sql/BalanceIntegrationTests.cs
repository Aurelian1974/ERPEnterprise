using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Reporting;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Balanta de verificare, fisa contului si cartea mare (02.02, C5 3.2, 3.3): soldurile initiale din nota de deschidere, rulajele precedente si
/// curente, egalitatile balantei, fisa 628 cu si fara nota de inchidere, contul corespondent in cartea mare.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BalanceIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly OctoberEnd = new(2026, 10, 31);

    public BalanceIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.ReportTrialBalance]);

    [Fact]
    [Trait("Criteriu", "02.02-C5-A1")]
    [Trait("Criteriu", "02.02-C5-A4")]
    public async Task The_trial_balance_adds_up_and_the_628_ledger_closes_to_zero_after_the_closing_entry()
    {
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var center = await CostCenter();

        await Entry(journal, new DateOnly(2026, 1, 1), [new(a["5121"], 1, "10000"), new(a["1012"], 2, "10000")], kind: 5);
        await Entry(journal, new DateOnly(2026, 9, 10), [new(a["628"], 1, "1000", CostCenterId: center), new(a["462"], 2, "1000")]);
        await Entry(journal, new DateOnly(2026, 10, 12), [new(a["628"], 1, "37940", CostCenterId: center), new(a["462"], 2, "37940")]);
        await Entry(journal, new DateOnly(2026, 10, 20), [new(a["462"], 1, "5000"), new(a["5121"], 2, "5000")]);
        await Entry(journal, OctoberEnd, [new(a["121"], 1, "38940"), new(a["628"], 2, "38940", CostCenterId: center)], kind: 4);

        // C5-A1: fiecare pereche de coloane e egala, soldul final = sumele totale; sinteticul de gradul I = suma analiticelor.
        var analytic = await Send(new RunTrialBalance(October, OctoberEnd));
        Assert.All(analytic.Checks, c => Assert.True(c.Passed, c.Name));
        var bank = analytic.Rows.Single(r => r.AccountCode == "5121");
        Assert.Equal(("10000.00", "0.00", "0.00", "5000.00", "5000.00", "0.00"), (bank.OpeningDebit, bank.PriorDebit, bank.PeriodDebit, bank.PeriodCredit, bank.ClosingDebit, bank.ClosingCredit));
        var expense = analytic.Rows.Single(r => r.AccountCode == "628");
        Assert.Equal(("1000.00", "37940.00", "38940.00", "0.00"), (expense.PriorDebit, expense.PeriodDebit, expense.PeriodCredit, expense.ClosingDebit));
        var grade1 = await Send(new RunTrialBalance(October, OctoberEnd, Level: 1));
        Assert.All(grade1.Checks, c => Assert.True(c.Passed, c.Name));
        Assert.Equal(analytic.Total.PeriodDebit, grade1.Total.PeriodDebit);
        Assert.Equal("5000.00", grade1.Rows.Single(r => r.AccountCode == "512").PeriodCredit);

        // C5-A4: fisa 628 pe octombrie se termina cu sold zero dupa nota de inchidere; fara notele de inchidere are sold debitor 38.940.
        var ledger = await Send(new GetAccountLedger("628", October, OctoberEnd));
        Assert.Equal(("1000.00", "0.00", 2), (ledger.Opening, ledger.Closing, ledger.LineCount));
        Assert.Equal(["38940.00", "0.00"], ledger.Lines.Select(l => l.Balance));
        var withoutClosing = await Send(new GetAccountLedger("628", October, OctoberEnd, IncludeClosingEntries: false));
        Assert.Equal("38940.00", withoutClosing.Closing);
        Assert.Equal("38940.00", (await Send(new RunTrialBalance(October, OctoberEnd, IncludeClosingEntries: false))).Rows.Single(r => r.AccountCode == "628").ClosingDebit);

        // Cartea mare: 628 debitat din 462, creditat prin 121; balanta nu trece peste doi ani.
        var book = await Send(new GetGeneralLedger(2026, 10));
        var line628 = book.Single(b => b.AccountCode == "628");
        Assert.Contains(line628.Moves, m => m is { CounterAccount: "462", Debit: "37940.00" });
        Assert.Contains(line628.Moves, m => m is { CounterAccount: "121", Credit: "38940.00" });
        Assert.Equal(("1000.00", "0.00"), (line628.OpeningDebit, line628.ClosingDebit));
        // Interogarile nu trec prin tranzactie: codul SQL devine FIN_BALANCE_CROSSES_YEARS in handlerul de erori al API-ului.
        var crosses = await Assert.ThrowsAnyAsync<SqlException>(() => Send(new RunTrialBalance(new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 31))));
        Assert.Equal(51973, crosses.Number);
    }

    private async Task Entry(int journal, DateOnly day, JournalLineInput[] lines, byte? kind = null)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test balanță", null, lines));
        if (kind is { } k)
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.ExecuteAsync("UPDATE fin.JournalEntry SET SourceKind = @K WHERE Id = @Id", new { K = k, draft.Id });
        }

        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Sediu', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("BL") });
    }
}
