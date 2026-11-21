using Dapper;
using Erp.Core.Application.Audit;
using Erp.Core.Application.Dimensions;
using Erp.Core.Contracts;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Tests.Sql;

/// <summary>Configurare › Audit pe SQL Server real (02.01, 11; macheta 07h): cautarea, inainte / dupa, vizibilitatea, Istoricul firmei.</summary>
public sealed partial class UserIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-11")]
    public async Task Audit_search_shows_the_record_with_before_and_after_values_and_the_company_history()
    {
        var code = Unique("AU").ToUpperInvariant();
        var created = await Send(new SaveDimensionValue(null, database.CompanyId, DimensionTypes.Project, code, "Proiect vechi", null, null, null, null,
            true, null));
        var row = (await Send(new GetDimensions(DimensionTypes.Project, database.CompanyId, code))).Values.Single();
        await Send(new SaveDimensionValue(created.Id, 0, DimensionTypes.Project, code, "Proiect nou", null, null, null, null, true, row.RowVersion));

        var page = await Send(new SearchAudit(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1), EntityType: "Core.DimensionValue",
            EntityId: created.Id));
        Assert.True(page.AllCompanies);
        Assert.Equal(2, page.Total);
        Assert.All(page.Entries, e => Assert.Equal(($"{code} · Proiect nou", "Core.Dimension.Save"), (e.RecordLabel, e.Action)));
        Assert.Equal([false, true], page.Entries.Select(e => e.IsFirst));
        Assert.Contains("Core.DimensionValue", page.EntityTypes);
        Assert.Contains(page.Users, u => u.Id == database.AdminId);

        var found = await Send(new SearchAudit(DateTimeOffset.UtcNow.AddHours(-1), Search: "Proiect vechi"));
        Assert.Contains(found.Entries, e => e.EntityId == created.Id);

        var detail = await Send(new GetAuditEntry(page.Entries[0].Id));
        Assert.Contains("Proiect nou", detail.Changes, StringComparison.Ordinal);
        Assert.Contains("Proiect vechi", detail.PreviousChanges, StringComparison.Ordinal);
        Assert.NotNull(detail.PreviousAt);
        var first = await Send(new GetAuditEntry(page.Entries[1].Id));
        Assert.Null(first.PreviousChanges);

        var history = await Send(new GetCompanyHistory(database.CompanyId));
        Assert.Contains(history, h => h.EntityType == "Core.DimensionValue" && h.EntityId == created.Id);
    }

    [Fact]
    [Trait("Criteriu", "02.01-11")]
    public async Task Audit_and_login_journal_are_hidden_from_users_without_the_audit_right_on_all_companies()
    {
        var userName = Unique("audit.");
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT INTO core.LoginEvent (OccurredAt, UserName, UserId, Success, FailureReason, IpAddress, UserAgent)
                VALUES (SYSUTCDATETIME(), @UserName, NULL, 0, 'BAD_PASSWORD', '10.0.4.27', N'Test');
                """, new { UserName = userName });
        }

        var logins = await Send(new SearchLoginEvents(DateTimeOffset.UtcNow.AddHours(-1), Search: userName));
        Assert.True(logins.AllCompanies);
        var login = Assert.Single(logins.Events);
        Assert.Equal((false, "BAD_PASSWORD", "10.0.4.27"), (login.Success, login.FailureReason, login.IpAddress));

        var outsider = await Send(Employee(await NewEmployee()));
        var someEntry = (await Send(new SearchAudit(PageSize: 1))).Entries.Single();
        var hidden = await Send(new SearchAudit(), outsider.Id);
        Assert.Equal((false, 0), (hidden.AllCompanies, hidden.Total));
        Assert.Empty((await Send(new SearchLoginEvents(Search: userName), outsider.Id)).Events);
        var notFound = await Assert.ThrowsAsync<ErpException>(() => Send(new GetAuditEntry(someEntry.Id), outsider.Id));
        Assert.Equal("CORE_ENTITY_NOT_FOUND", notFound.Code);
    }
}
