using Erp.Core.Application.Dimensions;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Tipuri de dimensiuni ale clientului, reguli implicite si combinatii blocate pe SQL Server real (02.01, 3.6 punctele 14 si 15).</summary>
public sealed partial class UserIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Custom_dimension_type_defaults_and_blocked_combinations_drive_document_validation()
    {
        var typeCode = "R" + Unique("").ToUpperInvariant();
        await Send(new SaveDimensionType(typeCode, "Regiune", 50, true, true, null));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveDimensionType(typeCode, "Alta", 50, true, true, null)));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);
        var system = (await Send(new GetDimensions(DimensionTypes.CostCenter, database.CompanyId))).Types.Single(t => t.Code == DimensionTypes.CostCenter);
        var protectedType = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveDimensionType(system.Code, system.Name, system.SortOrder, false, false, system.RowVersion)));
        Assert.Equal("CORE_INVALID_REFERENCE", protectedType.Code);

        var region = await Send(new SaveDimensionValue(null, database.CompanyId, typeCode, "NORD", "Nord", null, null, null, null, true, null));
        var costCenter = await Send(new SaveDimensionValue(null, database.CompanyId, DimensionTypes.CostCenter, Unique("CC").ToUpperInvariant(), "Vânzări",
            null, null, null, null, true, null));
        var item = await Send(await NewItem(Unique("DM")));

        var noValue = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SaveDefaultDimensions(2, item.Id,
            [new DefaultDimensionInput(DimensionTypes.CostCenter, null, (byte)DimensionPosting.Fixed)])));
        Assert.NotEmpty(noValue.Errors);
        await Send(new SaveDefaultDimensions(2, item.Id, [
            new DefaultDimensionInput(DimensionTypes.CostCenter, costCenter.Id, (byte)DimensionPosting.Fixed),
            new DefaultDimensionInput(typeCode, null, (byte)DimensionPosting.Mandatory),
        ]));
        var page = await Send(new GetDefaultDimensions(2, item.Id));
        Assert.Equal((byte?)3, page.Rows.Single(r => r.DimensionType == DimensionTypes.CostCenter).ValuePosting);
        Assert.Contains(page.Rows, r => r.DimensionType == typeCode && r.ValuePosting == 2);

        var defaults = await Use<IDimensionRules, IReadOnlyList<DimensionDefault>>(r => r.GetDefaultsAsync(database.CompanyId, null, item.Id, null, Ct));
        Assert.Equal((costCenter.Id, DimensionPosting.Fixed), (defaults.Single(d => d.DimensionType == DimensionTypes.CostCenter).ValueId,
            defaults.Single(d => d.DimensionType == DimensionTypes.CostCenter).Posting));

        await Send(new SaveDimensionCombination(typeCode, region.Id, DimensionTypes.CostCenter, costCenter.Id, "Nord nu vinde pe acest centru"));
        var issues = await Use<IDimensionRules, IReadOnlyList<DimensionIssue>>(r => r.ValidateAsync(database.CompanyId,
            new Dictionary<string, int?> { [DimensionTypes.CostCenter] = costCenter.Id, [typeCode] = region.Id }, null, item.Id, null, Ct));
        Assert.Equal("CORE_DIMENSION_COMBINATION_BLOCKED", Assert.Single(issues).Code);
        var missing = await Use<IDimensionRules, IReadOnlyList<DimensionIssue>>(r => r.ValidateAsync(database.CompanyId,
            new Dictionary<string, int?> { [DimensionTypes.CostCenter] = costCenter.Id }, null, item.Id, null, Ct));
        Assert.Equal("CORE_DIMENSION_REQUIRED", Assert.Single(missing).Code);

        var combination = Assert.Single(await Send(new GetDimensionCombinations()), c => c.TypeA == DimensionTypes.CostCenter || c.TypeB == DimensionTypes.CostCenter);
        await Send(new DeleteDimensionCombination(combination.Id));
        Assert.DoesNotContain(await Send(new GetDimensionCombinations()), c => c.Id == combination.Id);
    }
}
