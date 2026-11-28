using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Reporting;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>Situatiile financiare (02.02, C5 3.6, 4.2): F10 si F20 din definitiile livrate, echilibrul, generarea doar pe anul inchis, depunerea.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FinancialStatementIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public FinancialStatementIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.FiscalYearClose,
            FinancePermissions.ReportStatements, FinancePermissions.ReportStatementsSubmit]);

    [Fact]
    [Trait("Criteriu", "02.02-C5-A5")]
    public async Task The_statements_tie_the_profit_and_loss_to_a_balanced_balance_sheet_and_are_generated_only_on_a_closed_year()
    {
        await Send(new CreateFiscalYear(2026));
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var center = await CostCenter();
        var day = new DateOnly(2026, 3, 10);
        await Entry(journal, day, [new(a["5121"], 1, "10000"), new(a["1012"], 2, "10000")]);
        await Entry(journal, day, [new(a["5121"], 1, "5000"), new(a["707"], 2, "5000", CostCenterId: center)]);
        await Entry(journal, day, [new(a["628"], 1, "2000", CostCenterId: center), new(a["5121"], 2, "2000")]);
        await Entry(journal, day, [new(a["691"], 1, "480", CostCenterId: center), new(a["4411"], 2, "480")]);

        // C5-A5 (pe datele testului): rezultatul net din F20 = rezultatul exercitiului din F10, iar bilantul e echilibrat.
        await Send(new CalculateFinancialStatements(2026));
        var s = (await Send(new GetFinancialStatements(2026)))!;
        var rows = s.Lines.ToDictionary(l => l.RowCode, l => l.CurrentYear);
        Assert.Equal(("5000.00", "2520.00", "2520.00"), (rows["F20.01"], rows["F20.44"], rows["F10.20"]));
        Assert.Equal(("13000.00", "13000.00", "480.00"), (rows["F10.90"], rows["F10.91"], rows["F10.11"]));
        Assert.True(s.IsBalanced);
        Assert.Equal(1, s.EntityCategory);

        // Decizia 5: fisierul doar pe anul inchis (51976); apoi depunerea, dupa care situatiile nu se mai recalculeaza (51978).
        var open = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new GenerateFinancialStatements(2026, 1, s.RowVersion)));
        Assert.Equal("FIN_STATEMENTS_YEAR_NOT_CLOSED", open.Code);
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE fin.FiscalYear SET Status = 2 WHERE CompanyId = @Company AND Year = 2026", new { Company = Database.CompanyId });
        }

        var version = await Send(new GenerateFinancialStatements(2026, 1, s.RowVersion));
        Assert.Contains("SF_2026", (await Send(new DownloadFinancialStatements(2026, 1))).FileName, StringComparison.Ordinal);
        await Send(new SubmitFinancialStatements(2026, 1, version, "INT-2027-0001"));
        Assert.Equal((byte)4, (await Send(new GetFinancialStatements(2026)))!.Status);
        var submitted = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new CalculateFinancialStatements(2026)));
        Assert.Equal("FIN_STATEMENTS_ALREADY_SUBMITTED", submitted.Code);
    }

    private async Task Entry(int journal, DateOnly day, JournalLineInput[] lines)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test situații", null, lines));
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Sediu', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("SF") });
    }
}
