using Dapper;
using Erp.Auth;
using Erp.Auth.Data;
using Erp.Core.Application;
using Erp.Core.Application.Employees;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests.Sql;

/// <summary>Baza ERP_IT_USERS: administratorul integrat (ADMIN la nivelul instalarii), o firma si un partener extern.</summary>
public sealed class UserDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public int AdminId { get; private set; }

    public int CompanyId { get; private set; }

    public int PartnerId { get; private set; }

    public int AdminRoleId { get; private set; }

    public int StandardRoleId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_USERS", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        AdminId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy)
            VALUES (NEWID(), N'administrator', N'Administrator', 1, 1, 1);
            DECLARE @Id int = SCOPE_IDENTITY();
            INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
            SELECT @Id, r.Id, NULL, 1 FROM core.Role AS r WHERE r.Code = N'ADMIN';
            SELECT @Id;
            """);
        CompanyId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Company (Code, Name, FiscalCode, VatPayer, VatOnCollection, TradeRegisterNo, Address, City, CountyCode, CountryCode,
                DefaultPrecisionPolicyId, IncludeInConsolidation, IsActive, CreatedBy)
            SELECT N'UFIRMA', N'Firma utilizatori', N'18547290', 1, 0, N'J40/1234/2015', N'Str. 1', N'București', N'B', 'RO', Id, 1, 1, 1
            FROM core.PrecisionPolicy WHERE Code = N'STANDARD';
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        PartnerId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode,
                IsActive, CreatedBy)
            VALUES (N'AUDIT1', N'Cabinet Expert Audit SRL', 1, N'14399840', 0, 1, 1, N'Str. 2', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        AdminRoleId = await connection.ExecuteScalarAsync<int>("SELECT Id FROM core.Role WHERE Code = N'ADMIN'");
        StandardRoleId = await connection.ExecuteScalarAsync<int>("SELECT Id FROM core.Role WHERE Code = N'STANDARD'");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Administrarea utilizatorilor pe SQL Server real (02.01, 3.4, 8 si 11): angajati legati de fisa, externi limitati in timp, roluri pe
/// firma, contul de autentificare (ids) creat si actualizat in aceeasi tranzactie, actiunile de securitate, ultimul administrator
/// (A22), licenta si jobul zilnic de dezactivare.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed partial class UserIntegrationTests(UserDatabase database) : IClassFixture<UserDatabase>
{
    private readonly FakeLicense license = new();
    private readonly FakeBnrSource bnr = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> Send<T>(IRequest<T> request, int? userId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(typeof(UserIntegrationTests).Assembly);
        services.AddCoreInfrastructure(database.ConnectionString, new LicenseOptions(Development: true));
        services.AddSingleton<ILicenseInfo>(license);
        services.AddSingleton<IPasswordHasher<UserAccount>>(new PasswordHasher<UserAccount>());
        services.AddScoped<IIdentityAccountAdmin, IdentityAccountAdmin>();
        services.AddSingleton<Erp.Core.Application.Partners.IPersonalDataProtector>(
            new Erp.Core.Api.PersonalDataProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        var context = new FakeContext { UserId = userId ?? database.AdminId, CompanyId = database.CompanyId };
        context.Permissions.UnionWith([CorePermissions.UserManage, CorePermissions.RoleManage, CorePermissions.EmployeeView, CorePermissions.EmployeeManage,
            CorePermissions.ItemView, CorePermissions.ItemManage, CorePermissions.PartnerView, CorePermissions.PartnerManage,
            CorePermissions.ExchangeRateManageManual, CorePermissions.NomenclatureManage, CorePermissions.DimensionManage, CorePermissions.PrecisionManage]);
        services.AddSingleton<ICurrentContext>(context);
        services.AddSingleton<Erp.Core.Application.Me.ICurrentUserProfile>(context);
        services.AddScoped<HandlerCalls>();
        services.AddSingleton<Erp.Core.Application.ExchangeRates.IBnrRatesSource>(bnr);
        using var scope = services.BuildServiceProvider().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private async Task<int> NewEmployee(string firstName = "Maria", string lastName = "Ionescu", DateOnly? leaveDate = null)
    {
        var saved = await Send(new SaveEmployee(null, database.CompanyId, null, Unique("M"), firstName, lastName, "Vânzări", "Operator vânzări",
            $"{Unique("m")}@exemplu.ro", "0744 123 456", new DateOnly(2025, 2, 3), leaveDate, true, null));
        return saved.Id;
    }

    private SaveUser Employee(int employeeId, string? userName = null, IReadOnlyList<UserRoleInput>? roles = null) =>
        new(null, userName ?? Unique("maria."), 1, employeeId, null, null, null, null, null, null, null, null, null, null, "ro",
            database.CompanyId, true, null, roles);

    private SaveUser External(string? userName = null, string accessType = ExternalAccessTypes.Auditor, DateOnly? from = null, DateOnly? until = null) =>
        new(null, userName ?? Unique("elena."), 2, null, "Elena", "Constantin", $"{Unique("e")}@expertaudit.ro", "0722 987 654", accessType,
            database.PartnerId, database.AdminId, "Auditul situațiilor financiare 2026", from ?? Today, until ?? Today.AddDays(30), "ro", null, true,
            null);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<T> Query<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Employee_user_takes_name_from_employee_gets_temporary_password_and_one_account_per_employee()
    {
        var employeeId = await NewEmployee("Maria", "Ionescu");
        var created = await Send(Employee(employeeId, "Maria.Ionescu.T1"));

        Assert.NotNull(created.TemporaryPassword);
        Assert.Equal(TemporaryPassword.Length, created.TemporaryPassword!.Length);
        var detail = await Send(new GetUser(created.Id));
        Assert.Equal(("maria.ionescu.t1", "Maria Ionescu", true, employeeId), (detail.UserName, detail.DisplayName, detail.IsEmployee, detail.EmployeeId));
        Assert.True(detail.HasAccount);
        Assert.True(detail.MustChangePassword);
        Assert.False(detail.TwoFactorRequired);

        // Parola temporara e cea salvata in ids (hash Identity), iar contul se poate autentifica.
        var hash = await Query<string>("SELECT PasswordHash FROM ids.UserAccount WHERE UserName = N'maria.ionescu.t1'");
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<UserAccount>().VerifyHashedPassword(new UserAccount(), hash, created.TemporaryPassword));
        Assert.True(await Query<bool>("SELECT CanSignIn FROM core.v_UserLoginStatus WHERE UserId = @Id", new { created.Id }));

        var linked = await Assert.ThrowsAsync<ErpException>(() => Send(Employee(employeeId)));
        Assert.Equal("CORE_EMPLOYEE_ALREADY_LINKED", linked.Code);

        var duplicate = await Assert.ThrowsAsync<ErpException>(async () => await Send(Employee(await NewEmployee(), "MARIA.IONESCU.T1")));
        Assert.Equal("CORE_USER_DUPLICATE_USERNAME", duplicate.Code);

        var page = await Send(new SearchUsers(Search: "maria ion", Kind: 1));
        Assert.Contains(page.Items, i => i.Id == created.Id && i.TwoFactorEnabled == false);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task External_user_needs_reason_sponsor_and_limited_validity_and_requires_two_factor()
    {
        var incomplete = await Assert.ThrowsAsync<ErpValidationException>(() => Send(External() with { AccessReason = null, SponsorUserId = null }));
        Assert.Contains("AccessReason", incomplete.Errors.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SponsorUserId", incomplete.Errors.Keys, StringComparer.OrdinalIgnoreCase);

        var tooLong = await Assert.ThrowsAsync<ErpValidationException>(() => Send(External(until: Today.AddMonths(13))));
        Assert.Contains("validUntil", tooLong.Errors.Keys);

        var created = await Send(External());
        var detail = await Send(new GetUser(created.Id));
        Assert.Equal((false, ExternalAccessTypes.Auditor, database.PartnerId, database.AdminId, "Elena Constantin"),
            (detail.IsEmployee, detail.ExternalAccessType, detail.ExternalPartnerId, detail.SponsorUserId, detail.DisplayName));
        Assert.True(detail.TwoFactorRequired);

        var expiring = await Send(new SearchUsers(Filter: 2));
        Assert.Contains(expiring.Items, i => i.Id == created.Id);
        Assert.True(expiring.Counters.ExpiringCount >= 1);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Technical_account_has_no_password_and_cannot_get_one()
    {
        var created = await Send(External(accessType: ExternalAccessTypes.Technical) with { Email = null });
        Assert.Null(created.TemporaryPassword);
        Assert.False(await Query<bool>("SELECT HasPassword FROM ids.v_UserAccountSecurity AS s JOIN core.[User] AS u ON u.SubjectId = s.SubjectId WHERE u.Id = @Id",
            new { created.Id }));

        var reset = await Assert.ThrowsAsync<ErpException>(() => Send(new ResetUserPassword(created.Id)));
        Assert.Equal("CORE_USER_TECHNICAL_ACCOUNT", reset.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Roles_are_per_company_installation_scope_only_for_admin_and_admin_requires_two_factor()
    {
        var created = await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(database.StandardRoleId, database.CompanyId)]));
        var detail = await Send(new GetUser(created.Id));
        Assert.Equal((database.StandardRoleId, (int?)database.CompanyId), (Assert.Single(detail.Roles).RoleId, detail.Roles[0].CompanyId));
        Assert.False(detail.TwoFactorRequired);

        var invalid = await Assert.ThrowsAsync<ErpException>(() => Send(UpdateOf(detail) with { Roles = [new UserRoleInput(database.StandardRoleId, null)] }));
        Assert.Equal("CORE_ROLE_SCOPE_INVALID", invalid.Code);

        await Send(UpdateOf(detail) with { Roles = [new UserRoleInput(database.AdminRoleId, null), new UserRoleInput(database.StandardRoleId, database.CompanyId)] });
        detail = await Send(new GetUser(created.Id));
        Assert.Equal(2, detail.Roles.Count);
        Assert.True(detail.TwoFactorRequired);

        // Separarea drepturilor: utilizatorul nu-si schimba singur rolurile.
        var self = await Assert.ThrowsAsync<ErpException>(() => Send(UpdateOf(detail) with { Roles = [] }, userId: created.Id));
        Assert.Equal("CORE_USER_SELF_ROLES", self.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.01-A22")]
    public async Task Last_active_installation_administrator_cannot_be_deactivated_or_lose_the_role()
    {
        var other = await Send(Employee(await NewEmployee()));
        var admin = await Send(new GetUser(database.AdminId));

        // Alt administrator activ exista doar daca il cream; pana atunci administratorul integrat e ultimul.
        var activeAdmins = await Query<int>("""
            SELECT COUNT(*) FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
            JOIN core.v_UserLoginStatus AS s ON s.UserId = a.UserId
            WHERE r.Code = N'ADMIN' AND a.CompanyId IS NULL AND s.CanSignIn = 1
            """);
        if (activeAdmins == 1)
        {
            var deactivate = await Assert.ThrowsAsync<ErpException>(() => Send(UpdateOf(admin) with { IsActive = false }, userId: other.Id));
            Assert.Equal("CORE_LAST_ADMINISTRATOR", deactivate.Code);
            var removeRole = await Assert.ThrowsAsync<ErpException>(() => Send(UpdateOf(admin) with { Roles = [] }, userId: other.Id));
            Assert.Equal("CORE_LAST_ADMINISTRATOR", removeRole.Code);
        }

        var selfDeactivate = await Assert.ThrowsAsync<ErpValidationException>(() => Send(UpdateOf(admin) with { IsActive = false }));
        Assert.Contains("isActive", selfDeactivate.Errors.Keys);

        var system = await Assert.ThrowsAsync<ErpException>(() => Send(UpdateOf(admin) with { UserName = "admin2" }));
        Assert.Equal("CORE_SYSTEM_USER_PROTECTED", system.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Deactivation_closes_sessions_blocks_sign_in_and_publishes_the_event()
    {
        var created = await Send(Employee(await NewEmployee()));
        var subjectId = await Query<Guid>("SELECT SubjectId FROM core.[User] WHERE Id = @Id", new { created.Id });
        await CreateSession(subjectId);
        var stamp = await Query<string>("SELECT SecurityStamp FROM ids.UserAccount WHERE SubjectId = @subjectId", new { subjectId });

        var detail = await Send(new GetUser(created.Id));
        Assert.Equal(1, detail.ActiveSessions);
        Assert.Single(await Send(new GetUserSessions(created.Id)));

        await Send(UpdateOf(detail) with { IsActive = false });

        Assert.False(await Query<bool>("SELECT CanSignIn FROM core.v_UserLoginStatus WHERE UserId = @Id", new { created.Id }));
        Assert.Equal(0, await Query<int>("SELECT ActiveSessions FROM ids.v_UserAccountSecurity WHERE SubjectId = @subjectId", new { subjectId }));
        Assert.NotEqual(stamp, await Query<string>("SELECT SecurityStamp FROM ids.UserAccount WHERE SubjectId = @subjectId", new { subjectId }));
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox
            WHERE EventType = N'Erp.Core.Contracts.UtilizatorDezactivat' AND JSON_VALUE(Payload, '$.userId') = @Id
            """, new { Id = created.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        Assert.Contains(await Send(new GetUserHistory(created.Id)), h => h.Action == "Core.User.Save");
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Security_actions_reset_password_unlock_reset_two_factor_and_revoke_sessions()
    {
        var created = await Send(Employee(await NewEmployee()));
        var subjectId = await Query<Guid>("SELECT SubjectId FROM core.[User] WHERE Id = @Id", new { created.Id });
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("""
                UPDATE ids.UserAccount
                SET LockoutEndAt = DATEADD(MINUTE, 30, SYSUTCDATETIME()), AccessFailedCount = 5, TwoFactorEnabled = 1,
                    AuthenticatorKeyEncrypted = 0x01, MustChangePassword = 0
                WHERE SubjectId = @subjectId;
                """, new { subjectId });
        }

        await CreateSession(subjectId);
        var locked = await Send(new GetUser(created.Id));
        Assert.NotNull(locked.LockedUntil);

        Assert.True(await Send(new UnlockUser(created.Id)));
        Assert.Null((await Send(new GetUser(created.Id))).LockedUntil);
        Assert.Equal(1, (await Send(new GetUser(created.Id))).ActiveSessions);

        Assert.True(await Send(new ResetUserTwoFactor(created.Id)));
        var afterTwoFactor = await Send(new GetUser(created.Id));
        Assert.False(afterTwoFactor.TwoFactorEnabled);
        Assert.Equal(0, afterTwoFactor.ActiveSessions);

        var reset = await Send(new ResetUserPassword(created.Id));
        Assert.True((await Send(new GetUser(created.Id))).MustChangePassword);
        var hash = await Query<string>("SELECT PasswordHash FROM ids.UserAccount WHERE SubjectId = @subjectId", new { subjectId });
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<UserAccount>().VerifyHashedPassword(new UserAccount(), hash, reset.TemporaryPassword));

        var first = await CreateSession(subjectId);
        var second = await CreateSession(subjectId);
        Assert.True(await Send(new RevokeUserSessions(created.Id, first)));
        Assert.Equal(second, Assert.Single(await Send(new GetUserSessions(created.Id))).SessionId);
        Assert.True(await Send(new RevokeUserSessions(created.Id, null)));
        Assert.Empty(await Send(new GetUserSessions(created.Id)));

        var actions = (await Send(new GetUserHistory(created.Id))).Select(h => h.Action).ToList();
        Assert.Contains("Core.User.ResetPassword", actions);
        Assert.Contains("Core.User.Unlock", actions);
        Assert.Contains("Core.User.ResetTwoFactor", actions);
        Assert.Contains("Core.User.RevokeSessions", actions);
        Assert.DoesNotContain(await Query<string>("SELECT STRING_AGG(Changes, N'|') FROM core.AuditEntry WHERE EntityType = N'Core.User' AND EntityId = @Id",
            new { created.Id }), reset.TemporaryPassword, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task License_limit_counts_active_non_system_users()
    {
        var active = await Query<int>("SELECT COUNT(*) FROM core.[User] WHERE IsActive = 1 AND IsSystem = 0");
        license.Limits["users"] = active;
        var refused = await Assert.ThrowsAsync<ErpException>(async () => await Send(Employee(await NewEmployee())));
        Assert.Equal("CORE_USER_LICENSE_LIMIT", refused.Code);

        license.Limits["users"] = active + 1;
        var created = await Send(Employee(await NewEmployee()));
        Assert.True(created.Id > 0);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Employee_changes_flow_to_the_linked_user_and_leave_date_stops_access_then_the_job_deactivates()
    {
        var employeeId = await NewEmployee("Radu", "Dobre");
        var created = await Send(Employee(employeeId));
        var employee = await Send(new GetEmployee(employeeId));
        Assert.Equal(created.Id, employee.UserId);

        var newEmail = $"{Unique("radu")}@exemplu.ro";
        await Send(new SaveEmployee(employeeId, employee.CompanyId, null, employee.EmployeeNo, "Radu", "Dobrescu", employee.Department,
            employee.JobTitle, newEmail, employee.Phone, employee.HireDate, Today, true, employee.RowVersion));

        var user = await Send(new GetUser(created.Id));
        Assert.Equal(("Radu Dobrescu", newEmail), (user.DisplayName, user.Email));
        Assert.Equal(newEmail, await Query<string>("SELECT a.Email FROM ids.UserAccount AS a JOIN core.[User] AS u ON u.SubjectId = a.SubjectId WHERE u.Id = @Id",
            new { created.Id }));
        Assert.False(await Query<bool>("SELECT CanSignIn FROM core.v_UserLoginStatus WHERE UserId = @Id", new { created.Id }));

        await using var connection = new SqlConnection(database.ConnectionString);
        var deactivated = (await connection.QueryAsync<(int Id, Guid SubjectId, string Reason)>("core.usp_User_DeactivateExpired",
            new { Today = Today.ToDateTime(TimeOnly.MinValue), CorrelationId = Guid.NewGuid() }, commandType: System.Data.CommandType.StoredProcedure)).ToList();
        Assert.Contains(deactivated, d => d.Id == created.Id && d.Reason == "EmployeeLeft");
        Assert.False(await Query<bool>("SELECT IsActive FROM core.[User] WHERE Id = @Id", new { created.Id }));
        Assert.Contains(await Send(new GetUserHistory(created.Id)), h => h.Action == "Core.User.DeactivateExpired");
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Employee_number_is_unique_per_company_ignoring_case()
    {
        var number = Unique("MRC");
        await Send(new SaveEmployee(null, database.CompanyId, null, number, "Ion", "Popescu", null, null, null, null, null, null, true, null));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveEmployee(null, database.CompanyId, null, number.ToLowerInvariant(), "Ion", "Pop", null, null, null, null, null, null, true, null)));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);

        var found = await Send(new SearchEmployees(Search: "popescu ion"));
        Assert.Contains(found.Items, e => e.EmployeeNo == number);
    }

    [Fact]
    [Trait("Criteriu", "02.01-11.2")]
    public async Task Notification_preferences_are_saved_with_the_user_and_critical_types_stay_in_the_application()
    {
        var created = await Send(Employee(await NewEmployee()));
        var detail = await Send(new GetUser(created.Id));
        Assert.All(detail.NotificationPreferences, p => Assert.True(p.IsDefault));

        var critical = await Assert.ThrowsAsync<ErpValidationException>(() => Send(UpdateOf(detail) with
        {
            NotificationPreferences = [new NotificationPreferenceInput(NotificationTypes.AccountLocked, false, true, EmailModes.Immediate)],
        }));
        Assert.Contains($"notificationPreferences.{NotificationTypes.AccountLocked}", critical.Errors.Keys);

        await Send(UpdateOf(detail) with
        {
            NotificationPreferences = [new NotificationPreferenceInput(NotificationTypes.ImportCompleted, true, true, EmailModes.Immediate)],
        });
        detail = await Send(new GetUser(created.Id));
        var import = Assert.Single(detail.NotificationPreferences, p => p.Code == NotificationTypes.ImportCompleted);
        Assert.Equal((true, EmailModes.Immediate, false), (import.Email, import.EmailMode, import.IsDefault));

        // O salvare fara preferinte (null) le lasa neschimbate.
        await Send(UpdateOf(detail) with { Phone = null });
        Assert.False(Assert.Single((await Send(new GetUser(created.Id))).NotificationPreferences, p => p.Code == NotificationTypes.ImportCompleted).IsDefault);
        Assert.Contains(await Send(new GetUserLookups()).ContinueWith(t => t.Result.NotificationTypes), t => t.Code == NotificationTypes.OutboxFailed && t.Critical);
    }

    [Fact]
    [Trait("Criteriu", "02.01-8")]
    public async Task Role_save_replaces_permissions_and_system_roles_are_protected()
    {
        var code = Unique("OPV");
        var saved = await Send(new SaveRole(null, code, "Operatori vânzări", null,
            [await PermissionId(CorePermissions.PartnerView), await PermissionId(CorePermissions.ItemView)], null));
        var role = (await Send(new GetRoleMatrix())).Roles.Single(r => r.Id == saved.Id);
        Assert.Equal(new[] { await PermissionId(CorePermissions.PartnerView), await PermissionId(CorePermissions.ItemView) }.Order(), role.PermissionIds);

        var updated = await Send(new SaveRole(saved.Id, code, "Operatori vânzări", "Facturare",
            [await PermissionId(CorePermissions.PartnerView), await PermissionId(CorePermissions.PartnerManage)], saved.RowVersion));
        role = (await Send(new GetRoleMatrix())).Roles.Single(r => r.Id == saved.Id);
        Assert.Equal(new[] { await PermissionId(CorePermissions.PartnerView), await PermissionId(CorePermissions.PartnerManage) }.Order(), role.PermissionIds);
        Assert.Equal("Facturare", role.Description);

        var stale = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveRole(saved.Id, code, "Alt nume", null, [], saved.RowVersion)));
        Assert.Equal(CoreErrorCodes.ConcurrencyConflict, stale.Code);
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveRole(null, code.ToLowerInvariant(), "Dublură", null, [], null)));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);

        var matrix = await Send(new GetRoleMatrix());
        var admin = matrix.Roles.Single(r => r.Code == "ADMIN");
        Assert.True(admin.HasAllPermissions);
        var adminChange = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveRole(admin.Id, "ADMIN", admin.Name, null, [], admin.RowVersion)));
        Assert.Equal("CORE_ROLE_PROTECTED", adminChange.Code);
        var standard = matrix.Roles.Single(r => r.Code == "STANDARD");
        var codeChange = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveRole(standard.Id, "STANDARD2", standard.Name, null, standard.PermissionIds, standard.RowVersion)));
        Assert.Equal("CORE_ROLE_PROTECTED", codeChange.Code);
        var systemDelete = await Assert.ThrowsAsync<ErpException>(() => Send(new DeleteRole(standard.Id, standard.RowVersion)));
        Assert.Equal("CORE_ROLE_PROTECTED", systemDelete.Code);

        var user = await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(saved.Id, database.CompanyId)]));
        var inUse = await Assert.ThrowsAsync<ErpException>(() => Send(new DeleteRole(saved.Id, updated.RowVersion)));
        Assert.Equal("CORE_ENTITY_IN_USE", inUse.Code);
        Assert.Equal(1, (await Send(new GetRoleMatrix(database.CompanyId))).Roles.Single(r => r.Id == saved.Id).UserCount);

        await Send(UpdateOf(await Send(new GetUser(user.Id))) with { Roles = [] });
        Assert.True(await Send(new DeleteRole(saved.Id, updated.RowVersion)));
        Assert.DoesNotContain((await Send(new GetRoleMatrix())).Roles, r => r.Id == saved.Id);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.4")]
    public async Task Permission_overrides_add_and_revoke_rights_on_top_of_roles_and_revocation_wins()
    {
        var role = await Send(new SaveRole(null, Unique("GEST"), "Gestionari", null,
            [await PermissionId(CorePermissions.PartnerView), await PermissionId(CorePermissions.ItemView)], null));
        var user = await Send(Employee(await NewEmployee(), roles: [new UserRoleInput(role.Id, database.CompanyId)]));
        Assert.Equal([CorePermissions.ItemView, CorePermissions.PartnerView], await Effective(user.Id));

        var noReason = await Assert.ThrowsAsync<ErpValidationException>(async () => await Send(new SaveUserPermissionOverrides(user.Id, database.CompanyId,
            [new PermissionOverrideInput(await PermissionId(CorePermissions.PartnerManage), "G", " ")])));
        Assert.Contains(noReason.Errors.Values.SelectMany(v => v), m => m.Contains("Motivul", StringComparison.Ordinal));

        // Pe firma: Parteneri › modificare adaugat, Articole › vizualizare revocat.
        await Send(new SaveUserPermissionOverrides(user.Id, database.CompanyId,
        [
            new PermissionOverrideInput(await PermissionId(CorePermissions.PartnerManage), "G", "Înlocuiește colega în concediu"),
            new PermissionOverrideInput(await PermissionId(CorePermissions.ItemView), "R", null),
        ]));
        Assert.Equal([CorePermissions.PartnerManage, CorePermissions.PartnerView], await Effective(user.Id));

        // Pe toate firmele: revocarea castiga si asupra rolului; un drept adaugat se aplica pe orice firma.
        await Send(new SaveUserPermissionOverrides(user.Id, null,
        [
            new PermissionOverrideInput(await PermissionId(CorePermissions.PartnerView), "R", "Fără acces la parteneri"),
            new PermissionOverrideInput(await PermissionId(CorePermissions.CompanyView), "G", "Vede datele firmei"),
        ]));
        Assert.Equal([CorePermissions.CompanyView, CorePermissions.PartnerManage], await Effective(user.Id));

        var matrix = await Send(new GetRoleMatrix(database.CompanyId));
        var mine = matrix.Overrides.Where(o => o.UserId == user.Id).ToList();
        Assert.Equal(4, mine.Count);
        Assert.Equal(2, mine.Count(o => o.CompanyId is null));
        Assert.Contains(matrix.Users, u => u.Id == user.Id && u.RoleIds.SequenceEqual([role.Id]));
        Assert.Equal(2, (await Send(new GetRoleMatrix())).Overrides.Count(o => o.UserId == user.Id));

        // Lista goala pe firma sterge doar exceptiile firmei; cele pentru toate firmele raman.
        await Send(new SaveUserPermissionOverrides(user.Id, database.CompanyId, []));
        Assert.Equal([CorePermissions.CompanyView, CorePermissions.ItemView], await Effective(user.Id));

        var self = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveUserPermissionOverrides(user.Id, null, []), user.Id));
        Assert.Equal("CORE_USER_SELF_ROLES", self.Code);
        var admin = await Assert.ThrowsAsync<ErpException>(async () => await Send(new SaveUserPermissionOverrides(database.AdminId, null,
            [new PermissionOverrideInput(await PermissionId(CorePermissions.PartnerView), "R", null)]), user.Id));
        Assert.Equal("CORE_PERMISSION_OVERRIDE_INVALID", admin.Code);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.AuditEntry WHERE EntityType = N'Core.User' AND EntityId = @Id AND Action = N'Core.User.PermissionOverride' AND Changes LIKE N'%concediu%'",
            new { user.Id }));
    }

    private async Task<int> PermissionId(string code) => await Query<int>("SELECT Id FROM core.Permission WHERE Code = @code", new { code });

    /// <summary>Drepturile efective calculate la autentificare (core.usp_User_GetPermissions) pe firma de test, sortate.</summary>
    private async Task<string[]> Effective(int userId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var subjectId = await connection.ExecuteScalarAsync<Guid>("SELECT SubjectId FROM core.[User] WHERE Id = @userId", new { userId });
        await using var grid = await connection.QueryMultipleAsync("core.usp_User_GetPermissions", new { SubjectId = subjectId, database.CompanyId },
            commandType: System.Data.CommandType.StoredProcedure);
        Assert.NotNull(await grid.ReadSingleOrDefaultAsync());
        return [.. (await grid.ReadAsync<string>()).Order(StringComparer.Ordinal)];
    }

    private static SaveUser UpdateOf(UserDetailDto d) =>
        new(d.Id, d.UserName, (byte)(d.IsSystem ? 3 : d.IsEmployee ? 1 : 2), d.EmployeeId, d.FirstName, d.LastName, d.Email, d.Phone, d.ExternalAccessType, d.ExternalPartnerId,
            d.SponsorUserId, d.AccessReason, d.ValidFrom, d.ValidUntil, d.Language, d.LastCompanyId, d.IsActive, d.RowVersion);

    private async Task<Guid> CreateSession(Guid subjectId)
    {
        var sessionId = Guid.NewGuid();
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("""
            INSERT INTO ids.UserSession (SessionId, UserAccountId, IpAddress, UserAgent)
            SELECT @sessionId, Id, '10.0.0.1', N'Test' FROM ids.UserAccount WHERE SubjectId = @subjectId;
            """, new { sessionId, subjectId });
        return sessionId;
    }
}
