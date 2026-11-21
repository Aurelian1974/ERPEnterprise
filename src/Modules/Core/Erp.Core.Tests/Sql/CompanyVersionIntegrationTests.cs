using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Tests.Sql;

/// <summary>Datele firmei cu data de la care se aplica, pe SQL Server real (02.01, 3.6 punctul 2).</summary>
public sealed partial class OrganizationIntegrationTests
{
    private SaveCompany Change(CompanyDetailDto c, string name, bool vatOnCollection, DateOnly from) =>
        new(c.Id, c.GroupId, c.Code, name, c.FiscalCode, c.VatPayer, vatOnCollection, c.TradeRegisterNo, c.ShareCapital, c.Address, c.City, c.CountyCode,
            c.CountryCode, c.PostalCode, c.DefaultPrecisionPolicyId, c.IncludeInConsolidation, c.IsActive, c.RowVersion, c.Phone, c.Email, c.CitySirutaCode,
            EffectiveFrom: from);

    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Company_change_scheduled_for_later_applies_on_its_date_and_documents_see_the_data_of_their_date()
    {
        var created = await CreateCompanyAsync(Code());
        var original = await Send(new GetCompany(created.Id), created.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // O schimbare de azi se vede imediat, iar documentele mai vechi pastreaza denumirea de dinainte.
        await Send(Change(original, original.Name + " Nou", false, today), created.Id);
        var renamed = await Send(new GetCompany(created.Id), created.Id);
        Assert.Equal(original.Name + " Nou", renamed.Name);

        // TVA la incasare programat peste o luna: firma nu se schimba inca.
        var later = today.AddMonths(1);
        await Send(Change(renamed, renamed.Name, true, later), created.Id);
        var current = await Send(new GetCompany(created.Id), created.Id);
        Assert.False(current.VatOnCollection);

        var versions = await Send(new GetCompanyVersions(created.Id), created.Id);
        Assert.Equal([later, today, new DateOnly(1900, 1, 1)], versions.Select(v => v.ValidFrom));
        Assert.Equal(today.AddMonths(1).AddDays(-1), versions[1].ValidTo);

        await using var session = new DbSession(database.ConnectionString);
        var directory = new CompanyDirectory(new Infrastructure.Organization.CompanyVersionStore(session));
        Assert.Equal(original.Name, (await directory.GetAtAsync(created.Id, today.AddDays(-1), Ct)).Name);
        Assert.False((await directory.GetAtAsync(created.Id, today, Ct)).VatOnCollection);
        Assert.True((await directory.GetAtAsync(created.Id, later, Ct)).VatOnCollection);

        // Jobul zilnic, in ziua programata.
        await using var connection = new SqlConnection(database.ConnectionString);
        var applied = (await connection.QueryAsync<int>("core.usp_CompanyVersion_Apply",
            new { Today = later.ToDateTime(TimeOnly.MinValue), CompanyId = created.Id }, commandType: System.Data.CommandType.StoredProcedure)).ToList();
        Assert.Equal([created.Id], applied);
        Assert.True((await Send(new GetCompany(created.Id), created.Id)).VatOnCollection);
    }
}
