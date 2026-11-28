using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>Notele recurente (02.02, C2 3.8): sablonul genereaza o ciorna pe fiecare perioada scadenta, o singura data, iar ultima zi a lunii ramane ultima zi.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RecurringEntryIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public RecurringEntryIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate]);

    [Fact]
    public async Task A_monthly_template_creates_one_draft_per_due_month_at_month_end()
    {
        var accounts = (await Send(new GetGlAccounts())).ToDictionary(a => a.Code, a => a.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var center = await CostCenter();

        // Sablonul dezechilibrat e refuzat.
        await Assert.ThrowsAnyAsync<Exception>(() => Send(new SaveRecurringTemplate(null, "Chirie", journal, 1, new DateOnly(2026, 8, 31), null, null,
            [new(accounts["628"], 1, "1000", CostCenterId: center), new(accounts["462"], 2, "900")])));

        var id = await Send(new SaveRecurringTemplate(null, "Chirie depozit", journal, 1, new DateOnly(2026, 8, 31), new DateOnly(2026, 12, 31), "Chirie lunară",
            [new(accounts["628"], 1, "1000", CostCenterId: center), new(accounts["462"], 2, "1000")]));

        // Pana la 15.11: august, septembrie, octombrie (ultima zi a fiecarei luni); a doua rulare nu dubleaza.
        Assert.Equal(3, await Send(new GenerateRecurringEntries(new DateOnly(2026, 11, 15))));
        Assert.Equal(0, await Send(new GenerateRecurringEntries(new DateOnly(2026, 11, 15))));
        var dates = await Dates(id);
        Assert.Equal([new DateTime(2026, 8, 31), new DateTime(2026, 9, 30), new DateTime(2026, 10, 31)], dates);
        var template = Assert.Single(await Send(new GetRecurringTemplates()), t => t.Id == id);
        Assert.Equal(new DateOnly(2026, 11, 30), template.NextDate);

        // Ciorna se valideaza ca orice nota manuala.
        var last = await Send(new GetJournalEntry(template.LastEntryId!.Value));
        Assert.Equal(1, (int)last.Status);
        await Send(new ValidateManualEntry(last.Id, last.RowVersion));

        // Sablonul oprit nu mai genereaza; dupa data de sfarsit, nici cel pornit.
        await Send(new SetRecurringTemplateActive(id, false, template.RowVersion));
        Assert.Equal(0, await Send(new GenerateRecurringEntries(new DateOnly(2027, 3, 1))));
        template = Assert.Single(await Send(new GetRecurringTemplates()), t => t.Id == id);
        await Send(new SetRecurringTemplateActive(id, true, template.RowVersion));
        Assert.Equal(2, await Send(new GenerateRecurringEntries(new DateOnly(2027, 3, 1))));
    }

    private async Task<List<DateTime>> Dates(int templateId)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await connection.QueryAsync<DateTime>(
            "SELECT AccountingDate FROM fin.JournalEntry WHERE CompanyId = @Company AND SourceDocumentNumber = @Ref ORDER BY AccountingDate",
            new { Company = Database.CompanyId, Ref = $"Șablon #{templateId}" })).ToList();
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Logistică', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("LG") });
    }
}
