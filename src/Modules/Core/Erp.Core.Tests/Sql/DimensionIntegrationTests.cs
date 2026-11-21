using Dapper;
using Erp.Core.Application;
using Erp.Core.Application.Dimensions;
using Erp.Core.Contracts;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Tests.Sql;

/// <summary>Dimensiunile pe SQL Server real (02.01, 3.5; macheta 07k): coduri unice pe firma si tip, parinte, dezactivare, lista.</summary>
public sealed partial class UserIntegrationTests
{
    private SaveDimensionValue Dimension(string code, string type = DimensionTypes.CostCenter, int? parentId = null) =>
        new(null, database.CompanyId, type, code, "Valoare " + code, parentId, null, new DateOnly(2026, 1, 1), null, true, null);

    [Fact]
    [Trait("Criteriu", "02.01-3.5")]
    public async Task Dimension_values_have_unique_codes_a_parent_of_the_same_type_and_are_deactivated_not_deleted()
    {
        var prefix = Unique("CC");
        var parent = await Send(Dimension(prefix + "-VNZ"));
        var child = await Send(Dimension(prefix + "-VNZ-NV", parentId: parent.Id) with { ResponsibleUserId = database.AdminId });

        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(Dimension((prefix + "-vnz").ToLowerInvariant())));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);
        var sameCodeOtherType = await Send(Dimension(prefix + "-VNZ", DimensionTypes.Project));
        Assert.True(sameCodeOtherType.Id > 0);
        var wrongParent = await Assert.ThrowsAsync<ErpException>(() => Send(Dimension(prefix + "-P2", DimensionTypes.Project, parent.Id)));
        Assert.Equal("CORE_INVALID_REFERENCE", wrongParent.Code);

        var page = await Send(new GetDimensions(DimensionTypes.CostCenter, database.CompanyId, prefix));
        var row = page.Values.Single(v => v.Id == child.Id);
        Assert.Equal((prefix + "-VNZ", "Administrator", 0), (row.ParentCode, row.ResponsibleName, row.ActiveChildren));
        Assert.Equal(1, page.Values.Single(v => v.Id == parent.Id).ActiveChildren);
        Assert.Contains(page.Types, t => t.Code == DimensionTypes.Warehouse && t.ValueCount >= 0);

        // Ciclu: parintele nu poate deveni copilul propriului copil.
        var parentRow = page.Values.Single(v => v.Id == parent.Id);
        var cycle = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveDimensionValue(parent.Id, database.CompanyId, DimensionTypes.CostCenter,
            parentRow.Code, parentRow.Name, child.Id, null, null, null, true, parentRow.RowVersion)));
        Assert.Equal("CORE_INVALID_REFERENCE", cycle.Code);
        var withChildren = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveDimensionValue(parent.Id, database.CompanyId,
            DimensionTypes.CostCenter, parentRow.Code, parentRow.Name, null, null, null, null, false, parentRow.RowVersion)));
        Assert.Equal("CORE_NODE_HAS_ACTIVE_CHILDREN", withChildren.Code);

        var childRow = page.Values.Single(v => v.Id == child.Id);
        await Send(new SaveDimensionValue(child.Id, 0, DimensionTypes.Project, childRow.Code, childRow.Name, childRow.ParentId, null, null, null, false,
            childRow.RowVersion));
        var after = await Send(new GetDimensions(DimensionTypes.CostCenter, database.CompanyId, prefix, IncludeInactive: true));
        var deactivated = after.Values.Single(v => v.Id == child.Id);
        Assert.Equal((false, DimensionTypes.CostCenter, database.CompanyId), (deactivated.IsActive, deactivated.DimensionType, deactivated.CompanyId));
        Assert.DoesNotContain((await Send(new GetDimensions(DimensionTypes.CostCenter, database.CompanyId, prefix))).Values, v => v.Id == child.Id);

        // Modulele isi adauga valorile dupa cod (IDimensionLookup.EnsureAsync): a doua oara intoarce aceeasi valoare.
        await using var connection = new SqlConnection(database.ConnectionString);
        var ensure = new { database.CompanyId, DimensionType = DimensionTypes.Warehouse, Code = prefix + "-G1", Name = "Gestiune centrală", UserId = 1 };
        var first = await connection.ExecuteScalarAsync<int>("core.usp_DimensionValue_Ensure", ensure, commandType: System.Data.CommandType.StoredProcedure);
        var second = await connection.ExecuteScalarAsync<int>("core.usp_DimensionValue_Ensure", ensure with { Code = (prefix + "-g1").ToLowerInvariant() },
            commandType: System.Data.CommandType.StoredProcedure);
        Assert.Equal(first, second);
    }
}
