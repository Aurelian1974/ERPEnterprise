using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Contracts.Settlement;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>Modelele de contare (02.02, C2 3.2, 4.2; macheta c2-03): lista, variantele, simularea pe o nota reala si schimbarea liniilor unei variante.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostingModelIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day = new(2026, 11, 13);

    public PostingModelIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.PostingModelView, FinancePermissions.PostingModelManage, FinancePermissions.JournalEntryView]);

    [Fact]
    [Trait("Criteriu", "02.02-C2-A10")]
    public async Task The_simulation_gives_exactly_the_posted_lines_and_follows_a_changed_variant()
    {
        var center = await Scalar("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Vânzări', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("VZ") });
        await Scalar("INSERT INTO fin.CompanySetting (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, @Code, @Value, 1); SELECT 0;",
            new { Company = Database.CompanyId, Code = PostingService.DefaultCostCenterSetting, Value = center.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var partner = await Scalar("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Mega Retail SA', 1, N'12345678', 1, 0, 1, N'Str. Mare 1', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("MR-") });
        var sale = new FacturaEmisa(900_001, partner, "FV EXD 10241", Day, Day, "RON", 1m, 15004m, 15004m, 2604m, 2604m, false, false, null, false,
            [new InvoiceDueDate(Day.AddDays(30), 15004m)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
        var entryId = (await InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(PostingAdapters.SalesInvoice(sale), Ct))).EntryId!.Value;

        // C2-A10: simularea pe nota scrisa da exact liniile ei.
        var simulation = await Send(new SimulatePosting(entryId));
        var posted = (await Send(new GetJournalEntry(entryId))).Lines.Select(l => (l.AccountCode, (byte)l.Side, l.AmountRon, l.PartnerId, l.CostCenterId));
        Assert.Equal(posted, simulation.Lines.Select(l => (l.AccountCode, l.Side, l.AmountRon, l.PartnerId, l.CostCenterId)));

        // Lista si fisa modelului; schimbarea contului implicit al veniturilor in varianta Standard se vede in simulare.
        var item = Assert.Single(await Send(new GetPostingModels()), m => m.EventType == nameof(FacturaEmisa));
        Assert.Equal(2, item.VariantCount);
        var model = await Send(new GetPostingModel(item.Id));
        var standard = model.Variants.Single(v => v.Priority == 90);
        Assert.Contains(model.AccountKeys, k => k.Code == "Revenue");
        var lines = standard.Lines.Select(l => new PostingModelLineInput(l.Component, l.FilterVatCategory, l.FilterOperationTypeCode, l.Side, l.Sign, l.AccountKey,
            l.Component == "Net" ? "704" : l.DefaultAccountCode, l.CopyDimensions, l.GroupBy, l.DescriptionTemplate)).ToList();
        await Send(new SavePostingVariantLines(standard.Id, lines, standard.RowVersion));
        Assert.Contains((await Send(new SimulatePosting(entryId))).Lines, l => l.AccountCode == "704");
        var stale = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new SavePostingVariantLines(standard.Id, lines, standard.RowVersion)));
        Assert.Equal("FIN_CONCURRENCY_CONFLICT", stale.Code);
        var bad = await Assert.ThrowsAnyAsync<ErpException>(async () => await Send(new SavePostingVariantLines(standard.Id,
            [lines[0] with { DefaultAccountCode = "999999" }], (await Send(new GetPostingModel(item.Id))).Variants.Single(v => v.Priority == 90).RowVersion)));
        Assert.Equal("FIN_ENTRY_ACCOUNT_INVALID", bad.Code);
    }

    private async Task<int> Scalar(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }
}
