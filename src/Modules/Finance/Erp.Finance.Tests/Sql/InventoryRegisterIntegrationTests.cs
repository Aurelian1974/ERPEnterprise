using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Reporting;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>Registrul inventar (02.02, C5 3.4): elementele conturilor de bilant la sfarsitul anului, pe partener, si PDF-ul (provizoriu pe anul deschis).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class InventoryRegisterIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public InventoryRegisterIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.FiscalYearClose,
            FinancePermissions.ReportInventoryRegister]);

    [Fact]
    public async Task The_register_lists_balance_sheet_accounts_by_partner_with_a_pdf()
    {
        await Send(new CreateFiscalYear(2026));
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        int partner;
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            partner = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
                VALUES (@Code, N'Client Inventar SRL', 1, @Fiscal, 1, 0, 1, N'Str. Lungă 3', N'Brașov', N'BV', 'RO', 1, 1);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, new { Code = Unique("IR-"), Fiscal = Unique("RO9") });
        }

        await Entry(journal, new DateOnly(2026, 2, 1), [new(a["5121"], 1, "8000"), new(a["1012"], 2, "8000")]);
        await Entry(journal, new DateOnly(2026, 2, 2), [new(a["4111"], 1, "1500", PartnerId: partner), new(a["5121"], 2, "1500")]);

        Erp.Pdf.PdfSetup.Configure(null);
        await Send(new GenerateInventoryRegister(2026));
        var register = (await Send(new GetInventoryRegister(2026)))!;
        Assert.False(register.IsFinal);
        Assert.Contains(register.Lines, l => l is { AccountCode: "4111", PartnerName: "Client Inventar SRL", Side: 1, BookValue: "1500.00", Difference: "0.00" });
        Assert.Contains(register.Lines, l => l is { AccountCode: "1012", Side: 2, BookValue: "8000.00" });
        Assert.DoesNotContain(register.Lines, l => l.AccountCode.StartsWith('6') || l.AccountCode.StartsWith('7'));
        var pdf = await Send(new DownloadInventoryRegister(2026));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
    }

    private async Task Entry(int journal, DateOnly day, JournalLineInput[] lines)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test inventar", null, lines));
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
    }
}
