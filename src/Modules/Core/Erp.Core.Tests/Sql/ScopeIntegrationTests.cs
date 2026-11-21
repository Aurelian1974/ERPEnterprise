using Dapper;
using Erp.Core.Application;
using Erp.Core.Application.Items;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Tests.Sql;

/// <summary>
/// Domeniul drepturilor si proprietarul articolelor / partenerilor pe SQL Server real (02.01, 3.2-3.4; deciziile din 21.11.2026):
/// rol pe grup, firma, punct de lucru sau locatie; articole comune sau proprii unui nod; vizibilitatea si dreptul de modificare.
/// </summary>
public sealed partial class UserIntegrationTests
{
    private sealed record Org(int GroupId, int OtherCompanyId, int Branch1, int Branch2, int Location1, int Location0, int RoleId);

    private async Task<Org> NewOrg()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var tag = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var ids = await connection.QuerySingleAsync<(int GroupId, int OtherCompanyId, int Branch1, int Branch2, int Location1, int Location0)>("""
            DECLARE @G int, @Z int, @B1 int, @B2 int, @L1 int, @L0 int;
            INSERT INTO core.CompanyGroup (Code, Name, CreatedBy) VALUES (N'G' + @Tag, N'Grup ' + @Tag, 1);
            SET @G = SCOPE_IDENTITY();
            UPDATE core.Company SET GroupId = @G WHERE Id = @CompanyId;
            INSERT INTO core.Company (Code, Name, FiscalCode, VatPayer, VatOnCollection, TradeRegisterNo, Address, City, CountyCode, CountryCode,
                DefaultPrecisionPolicyId, IncludeInConsolidation, IsActive, GroupId, CreatedBy)
            SELECT N'Z' + @Tag, N'Firma Z ' + @Tag, @FiscalCode, 0, 0, N'J12/345/2020', N'Str. 3', N'Cluj-Napoca', N'CJ', 'RO', Id, 1, 1, @G, 1
            FROM core.PrecisionPolicy WHERE Code = N'STANDARD';
            SET @Z = SCOPE_IDENTITY();
            INSERT INTO core.Branch (CompanyId, Code, Name, Address, City, CountyCode, CountryCode, CreatedBy)
            VALUES (@CompanyId, N'B1' + @Tag, N'Punct Nord ' + @Tag, N'Str. 4', N'București', N'B', 'RO', 1);
            SET @B1 = SCOPE_IDENTITY();
            INSERT INTO core.Branch (CompanyId, Code, Name, Address, City, CountyCode, CountryCode, CreatedBy)
            VALUES (@CompanyId, N'B2' + @Tag, N'Punct Sud ' + @Tag, N'Str. 5', N'București', N'B', 'RO', 1);
            SET @B2 = SCOPE_IDENTITY();
            INSERT INTO core.Location (CompanyId, BranchId, Code, Name, LocationType, CreatedBy)
            VALUES (@CompanyId, @B1, N'L1' + @Tag, N'Magazin Nord ' + @Tag, 3, 1);
            SET @L1 = SCOPE_IDENTITY();
            INSERT INTO core.Location (CompanyId, BranchId, Code, Name, LocationType, CreatedBy)
            VALUES (@CompanyId, NULL, N'L0' + @Tag, N'Depozit central ' + @Tag, 2, 1);
            SET @L0 = SCOPE_IDENTITY();
            SELECT @G, @Z, @B1, @B2, @L1, @L0;
            """, new { Tag = tag, database.CompanyId, FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var role = await Send(new SaveRole(null, Unique("NOM"), "Nomenclatoare locale", null,
        [
            await PermissionId(CorePermissions.ItemView), await PermissionId(CorePermissions.ItemManage),
            await PermissionId(CorePermissions.PartnerView), await PermissionId(CorePermissions.PartnerManage),
        ], null));
        return new Org(ids.GroupId, ids.OtherCompanyId, ids.Branch1, ids.Branch2, ids.Location1, ids.Location0, role.Id);
    }

    private async Task<SaveItem> NewItem(string prefix, byte? ownerLevel = null, int? ownerNodeId = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var set = await connection.ExecuteScalarAsync<int>("SELECT TOP (1) Id FROM core.UomSet WHERE IsActive = 1 ORDER BY Id");
        var vat = await connection.ExecuteScalarAsync<int>("SELECT TOP (1) Id FROM core.VatRate WHERE ValidTo IS NULL ORDER BY [Percent] DESC");
        return new SaveItem(null, Unique(prefix), "Articol " + prefix, 1, set, vat, null, null, null, true, null, null, null, null, null, true, null,
            OwnerLevel: ownerLevel, OwnerNodeId: ownerNodeId);
    }

    private async Task<string[]> VisibleItems(string prefix, int userId) =>
        [.. (await Send(new SearchItems(Search: prefix, PageSize: 200), userId)).Items.Select(i => i.Name).Order(StringComparer.Ordinal)];

    [Fact]
    [Trait("Criteria", "02.01-3.4")]
    public async Task Roles_on_group_branch_or_location_cover_their_companies_and_nodes()
    {
        var org = await NewOrg();
        var groupUser = await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(org.RoleId, null, CompanyGroupId: org.GroupId)]));
        var branchUser = await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(org.RoleId, null, BranchId: org.Branch1)]));

        var detail = await Send(new GetUser(branchUser.Id));
        var assignment = Assert.Single(detail.Roles);
        Assert.Equal((database.CompanyId, org.Branch1), (assignment.CompanyId, assignment.BranchId));
        Assert.Equal(org.GroupId, Assert.Single((await Send(new GetUser(groupUser.Id))).Roles).CompanyGroupId);

        await using var connection = new SqlConnection(database.ConnectionString);
        var companies = (await connection.QueryAsync<int>("SELECT CompanyId FROM core.fn_UserCompanies(@Id, N'Core.Item.Manage')", new { groupUser.Id })).ToHashSet();
        Assert.Superset(new HashSet<int> { database.CompanyId, org.OtherCompanyId }, companies);
        Assert.Equal([database.CompanyId], await connection.QueryAsync<int>("SELECT CompanyId FROM core.fn_UserCompanies(@Id, NULL)", new { branchUser.Id }));
        Assert.Contains(CorePermissions.ItemManage, await Effective(branchUser.Id));

        // Doar administratorul se atribuie pe toata instalarea; un punct de lucru si o locatie nu se aleg impreuna.
        var installation = await Assert.ThrowsAsync<ErpException>(async () => await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(org.RoleId, null)])));
        Assert.Equal("CORE_ROLE_SCOPE_INVALID", installation.Code);
        await Assert.ThrowsAsync<ErpValidationException>(async () => await Send(Employee(await NewEmployee(),
            roles: [new UserRoleInput(org.RoleId, null, BranchId: org.Branch1, LocationId: org.Location1)])));

        var matrix = await Send(new GetRoleMatrix(org.OtherCompanyId));
        Assert.Contains(matrix.Users, u => u.Id == groupUser.Id);
        Assert.DoesNotContain(matrix.Users, u => u.Id == branchUser.Id);
    }

    [Fact]
    [Trait("Criteria", "02.01-3.3")]
    public async Task Items_belong_to_the_group_or_a_node_and_users_see_and_change_only_their_nodes()
    {
        var org = await NewOrg();
        var branchUser = (await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(org.RoleId, null, BranchId: org.Branch1)]))).Id;
        var locationUser = (await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(org.RoleId, null, LocationId: org.Location1)]))).Id;
        var prefix = Unique("VIZ");

        // Administratorul (toata instalarea) adauga oriunde in firma curenta.
        await Send((await NewItem(prefix)) with { Name = prefix + " comun" });
        await Send((await NewItem(prefix, OwnerLevels.Company)) with { Name = prefix + " firma" });
        await Send((await NewItem(prefix, OwnerLevels.Branch, org.Branch2)) with { Name = prefix + " sud" });
        await Send((await NewItem(prefix, OwnerLevels.Location, org.Location0)) with { Name = prefix + " depozit" });

        // Utilizatorul punctului de lucru adauga doar in punctul lui si in locatiile lui.
        var north = await Send((await NewItem(prefix, OwnerLevels.Branch, org.Branch1)) with { Name = prefix + " nord" }, branchUser);
        await Send((await NewItem(prefix, OwnerLevels.Location, org.Location1)) with { Name = prefix + " magazin" }, branchUser);
        foreach (var denied in new[] { await NewItem(prefix), await NewItem(prefix, OwnerLevels.Company), await NewItem(prefix, OwnerLevels.Location, org.Location0) })
        {
            var error = await Assert.ThrowsAsync<ErpException>(() => Send(denied, branchUser));
            Assert.Equal("CORE_OWNER_ACCESS_DENIED", error.Code);
        }

        string[] Names(params string[] suffixes) => [.. suffixes.Select(s => prefix + " " + s).Order(StringComparer.Ordinal)];
        Assert.Equal(Names("comun", "firma", "nord", "magazin"), await VisibleItems(prefix, branchUser));
        Assert.Equal(Names("comun", "firma", "nord", "magazin"), await VisibleItems(prefix, locationUser));
        Assert.Equal(Names("comun", "firma", "sud", "depozit", "nord", "magazin"), await VisibleItems(prefix, database.AdminId));
        Assert.Equal(Names("comun"), (await Send(new SearchItems(Search: prefix, Owner: 1))).Items.Select(i => i.Name).ToArray());
        Assert.Equal(Names("nord", "magazin"), (await Send(new SearchItems(Search: prefix, OwnerBranchId: org.Branch1))).Items.Select(i => i.Name)
            .Order(StringComparer.Ordinal).ToArray());

        // Locatia vede articolul punctului ei de lucru, dar nu il modifica; punctul de lucru nu vede alt punct de lucru.
        var item = await Send(new GetItem(north.Id), locationUser);
        Assert.Equal((OwnerLevels.Branch, (int?)org.Branch1), (item.OwnerLevel, item.OwnerNodeId));
        var edit = await Assert.ThrowsAsync<ErpException>(() => Send(UpdateOf(item), locationUser));
        Assert.Equal("CORE_OWNER_ACCESS_DENIED", edit.Code);
        var south = (await Send(new SearchItems(Search: prefix + " sud"))).Items.Single();
        var hidden = await Assert.ThrowsAsync<ErpException>(() => Send(new GetItem(south.Id), branchUser));
        Assert.Equal("CORE_ENTITY_NOT_FOUND", hidden.Code);

        var options = await Send(new GetOwnerOptions(OwnedEntities.Item), branchUser);
        Assert.NotNull(options);
        Assert.False(options.ManageWholeCompany);
        Assert.Equal([org.Branch1], options.Nodes.Where(n => n.CanManage && n.BranchId is not null).Select(n => n.BranchId!.Value).ToArray());
        Assert.Equal([org.Location1], options.Nodes.Where(n => n.CanManage && n.LocationId is not null).Select(n => n.LocationId!.Value).ToArray());
    }

    [Fact]
    [Trait("Criteria", "02.01-3.2")]
    public async Task Duplicate_fiscal_code_names_the_existing_partner_and_where_it_belongs()
    {
        var org = await NewOrg();
        await using var connection = new SqlConnection(database.ConnectionString);
        var partner = await connection.QuerySingleAsync<(string Code, string Name)>("SELECT Code, Name FROM core.Partner WHERE Id = @Id", new { Id = database.PartnerId });
        SavePartner Duplicate() => new(null, Unique("DUP"), "Expert Audit duplicat", 1, "14399840", false, true, null, true, false, false, null, "Str. 9",
            "Cluj-Napoca", "CJ", "RO", null, null, null, null, true, null);

        var common = await Assert.ThrowsAsync<ErpException>(() => Send(Duplicate()));
        Assert.Equal("CORE_PARTNER_DUPLICATE_FISCAL_CODE", common.Code);
        Assert.Contains($"„{partner.Name}” (cod {partner.Code})", common.Message, StringComparison.Ordinal);
        Assert.Contains("partener comun", common.Message, StringComparison.Ordinal);

        await connection.ExecuteAsync("UPDATE core.Partner SET OwnerCompanyId = @CompanyId, OwnerBranchId = @Branch1 WHERE Id = @Id",
            new { database.CompanyId, org.Branch1, Id = database.PartnerId });
        try
        {
            var owned = await Assert.ThrowsAsync<ErpException>(() => Send(Duplicate()));
            Assert.Contains("Punct Nord", owned.Message, StringComparison.Ordinal);
            Assert.Contains("poate fi folosit pe facturi, NIR", owned.Message, StringComparison.Ordinal);
        }
        finally
        {
            await connection.ExecuteAsync("UPDATE core.Partner SET OwnerCompanyId = NULL, OwnerBranchId = NULL WHERE Id = @Id", new { Id = database.PartnerId });
        }
    }

    private static SaveItem UpdateOf(ItemDetailDto d) =>
        new(d.Id, d.Code, d.Name + " (modificat)", d.ItemType, d.UomSetId, d.DefaultVatRateId, d.ItemGroupId, d.PricePrecision, d.QuantityPrecision,
            d.IsStockable, d.CnCode, d.DepositItemId, d.NetWeightKg, d.GrossWeightKg, d.VolumeM3, d.IsActive, d.RowVersion);
}
