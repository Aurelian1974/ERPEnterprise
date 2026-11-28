using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Fx;
using Erp.Finance.Tests.Bank;

namespace Erp.Finance.Tests.Sql;

/// <summary>Conturile de trezorerie in valuta (02.02, F4 3.4, 3.5, 4.3): schimbul valutar intre conturi proprii si reevaluarea soldului.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FxTreasuryIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    public FxTreasuryIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.BankView, FinancePermissions.BankStatementImport, FinancePermissions.BankStatementReconcile,
            FinancePermissions.BankStatementValidate, FinancePermissions.BankStatementUnvalidate, FinancePermissions.FxView, FinancePermissions.FxRevalue]);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F4-A6")]
    [Trait("Criteriu", "02.02-F4-A10")]
    public async Task A_currency_exchange_at_the_bank_rate_realizes_the_difference_to_bnr_and_the_revalued_account_is_worth_balance_times_rate()
    {
        var company = Database.FxBankCompanyId;
        var (day, monthEnd) = (new DateOnly(2023, 10, 2), new DateOnly(2023, 10, 31));
        // Luni 02.10.2023 se foloseste cursul publicat vineri (4,97), nu cel de luni; reevaluarea foloseste cursul din 31.10, inclusiv.
        foreach (var (date, rate) in new[] { (day.AddDays(-4), 4.96m), (day.AddDays(-3), 4.97m), (day, 4.99m), (monthEnd, 5.00m) })
        {
            await Query<int>("""
                MERGE core.ExchangeRate AS t USING (SELECT 'EUR' AS CurrencyCode, @Day AS RateDate) AS s ON t.CurrencyCode = s.CurrencyCode AND t.RateDate = s.RateDate
                WHEN MATCHED THEN UPDATE SET Rate = @Rate
                WHEN NOT MATCHED THEN INSERT (CurrencyCode, RateDate, Rate, Multiplier, Source, CreatedBy) VALUES ('EUR', @Day, @Rate, 1, 'BNR', 1);
                SELECT @@ROWCOUNT
                """, new { Day = date.ToDateTime(TimeOnly.MinValue), Rate = rate });
        }

        var (ron, ronIban) = await NewBank("RON", company);
        var (eur, eurIban) = await NewBank("EUR", company);
        var ronLine = await SingleLine(ron, ronIban, "RON", 10_000m, new TestEntry(day, -5020m, Unique("SV"), null, null, "Cumparare EUR"), company);
        var eurLine = await SingleLine(eur, eurIban, "EUR", 0m, new TestEntry(day, 1000m, Unique("SV"), null, null, "Cumparare EUR"), company);
        var exchange = await OperationType("BANK-SCHIMB-VALUTAR");
        var ronExplained = await Send(new ExplainBankStatementLine(ronLine.Id, [Part(exchange, "-5020", eur)], ronLine.RowVersion), companyId: company);
        var eurExplained = await Send(new ExplainBankStatementLine(eurLine.Id, [Part(exchange, "1000", ron)], eurLine.RowVersion), companyId: company);

        // A6: 5.020 lei platiti pentru 1.000 EUR la BNR 4,97 (4.970): pierdere realizata 50, la validarea celei de-a doua linii.
        await Send(new ValidateBankStatementLines([new BankLineVersion(ronLine.Id, ronExplained.RowVersion)]), companyId: company);
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM fin.FxDifference WHERE CompanyId = @Company AND Source = 2", new { Company = company }));
        await Send(new ValidateBankStatementLines([new BankLineVersion(eurLine.Id, eurExplained.RowVersion)]), companyId: company);
        Assert.Equal((-50m, "665", 5020m, 4970m), await Query<(decimal, string, decimal, decimal)>(
            "SELECT DifferenceRon, GlAccountCode, CarryingAmountRon, SettlementAmountRon FROM fin.FxDifference WHERE CompanyId = @Company AND Source = 2",
            new { Company = company }));

        // A10: contul in euro valoreaza 4.970 lei; dupa reevaluarea lunii la 5,00 valoreaza exact 1.000 × 5,00.
        var before = Assert.Single(await Send(new GetTreasuryFxValues(monthEnd, eur), companyId: company));
        Assert.Equal(("1000.00", "4970.00"), (before.Balance, before.CarryingAmountRon));
        var runId = await Send(new GenerateFxRevaluation(2023, 10), companyId: company);
        var run = await Send(new GetFxRevaluation(runId), companyId: company);
        var line = Assert.Single(run.Lines, l => l.ElementKind == 2);
        Assert.Equal(("1000.00", "4970.00", "5000.00", "30.00"), (line.ForeignAmount, line.CarryingAmountRon, line.RevaluedAmountRon, line.DifferenceRon));
        await Send(new ValidateFxRevaluation(runId, run.RowVersion), companyId: company);
        var after = Assert.Single(await Send(new GetTreasuryFxValues(monthEnd, eur), companyId: company));
        Assert.Equal(("5000.00", "0.00"), (after.CarryingAmountRon, after.UnrealizedRon));

        // Desfacerea validarii unei linii inverseaza diferenta de schimb.
        await Send(new UnvalidateBankStatementLine(eurLine.Id, "Test", (await Send(new GetBankStatementLine(eurLine.Id), companyId: company)).RowVersion),
            companyId: company);
        Assert.Equal(0m, await Query<decimal>("SELECT SUM(DifferenceRon) FROM fin.FxDifference WHERE CompanyId = @Company AND Source = 2", new { Company = company }));
    }

    private static BankLinePartInput Part(int operationType, string amount, int counterAccount) =>
        new(operationType, amount, null, null, null, null, counterAccount, null, null, null, null);

    private async Task<BankStatementLineDto> SingleLine(int account, string iban, string currency, decimal opening, TestEntry entry, int company)
    {
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, currency, opening, [entry], "1"), companyId: company)).Statements);
        return Assert.Single((await Send(new GetBankStatement(imported.BankStatementId), companyId: company)).Lines);
    }
}
