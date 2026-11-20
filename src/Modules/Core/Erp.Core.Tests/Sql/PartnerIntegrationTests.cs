using System.Text;
using Dapper;
using Erp.Core.Api;
using Erp.Core.Application;
using Erp.Core.Application.NumberSeries;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests.Sql;

/// <summary>Baza ERP_IT_PARTNER: un administrator la nivelul instalarii si o firma cu seria de coduri „Core.Partner”.</summary>
public sealed class PartnerDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public int AdminId { get; private set; }

    public int CompanyId { get; private set; }

    /// <summary>Acelasi inel de chei pentru toate cererile testului (ca inelul din baza in productie).</summary>
    public IDataProtectionProvider DataProtection { get; } = new EphemeralDataProtectionProvider();

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_PARTNER", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        AdminId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy)
            VALUES (NEWID(), N'admin.partner', N'Admin Parteneri', 1, 1, 1);
            DECLARE @Id int = SCOPE_IDENTITY();
            INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy)
            SELECT @Id, r.Id, NULL, 1 FROM core.Role AS r WHERE r.Code = N'ADMIN';
            SELECT @Id;
            """);
        CompanyId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Company (Code, Name, FiscalCode, VatPayer, VatOnCollection, TradeRegisterNo, Address, City, CountyCode, CountryCode,
                DefaultPrecisionPolicyId, IncludeInConsolidation, IsActive, CreatedBy)
            SELECT N'PFIRMA', N'Firma parteneri', N'18547290', 1, 0, N'J40/1234/2015', N'Str. 1', N'București', N'B', 'RO', Id, 1, 1, 1
            FROM core.PrecisionPolicy WHERE Code = N'STANDARD';
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Partenerii pe SQL Server real (02.01, 3.2): CUI invalid sau duplicat refuzat (A9), cod unic fara majuscule (A21) sau alocat din
/// serie, CNP criptat si citit doar cu permisiune, jurnalizat (A13), proprietati configurabile, cautare fara diacritice, istoric.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PartnerIntegrationTests(PartnerDatabase database) : IClassFixture<PartnerDatabase>
{
    private const string Cnp = "1800101221144";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> Send<T>(IRequest<T> request, int? companyId = null, params string[] without)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(typeof(PartnerIntegrationTests).Assembly);
        services.AddCoreInfrastructure(database.ConnectionString, new LicenseOptions(Development: true));
        services.AddSingleton<IPersonalDataProtector>(new PersonalDataProtector(database.DataProtection));
        var context = new FakeContext { UserId = database.AdminId, CompanyId = companyId ?? database.CompanyId };
        context.Permissions.UnionWith([CorePermissions.PartnerView, CorePermissions.PartnerManage, CorePermissions.PartnerViewPersonalData,
            CorePermissions.NomenclatureManage, CorePermissions.NumberSeriesManage]);
        context.Permissions.ExceptWith(without);
        services.AddSingleton<ICurrentContext>(context);
        services.AddSingleton<Erp.Core.Application.Me.ICurrentUserProfile>(context);
        services.AddScoped<HandlerCalls>();
        using var scope = services.BuildServiceProvider().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    private static int nextFiscal = 300;

    private static string NewFiscalCode()
    {
        for (var body = Interlocked.Increment(ref nextFiscal) * 1000; ; body++)
        {
            for (var control = 0; control <= 9; control++)
            {
                var candidate = $"{body}{control}";
                if (Erp.Core.Domain.RomanianFiscalCode.IsValid(candidate))
                {
                    return candidate;
                }
            }
        }
    }

    private static string Code() => "P" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static SavePartner Legal(string? code, string name, string fiscalCode, bool customer = true, bool supplier = false,
        IReadOnlyList<PartnerPropertyValue>? properties = null) =>
        new(null, code, name, 1, fiscalCode, customer, supplier, null, true, false, false, "J33/567/2012", "Str. Gării nr. 21", "Suceava", "SV", "RO",
            "720004", null, null, null, true, null, Properties: properties);

    private async Task<PartnerLookupsDto> Lookups() => await Send(new GetPartnerLookups());

    private async Task<int> PropertyId(string code) => (await Lookups()).Properties.Single(p => p.Code == code).Id;

    private async Task<IReadOnlyList<PartnerPropertyValue>> Segment(string option = "RETAIL")
    {
        var lookups = await Lookups();
        var segment = lookups.Properties.Single(p => p.Code == "SEGMENT");
        return [new(segment.Id, ValueOptionId: lookups.Options.Single(o => o.PropertyDefinitionId == segment.Id && o.Code == option).Id)];
    }

    [Fact]
    [Trait("Criteriu", "02.01-A9")]
    public async Task Invalid_or_duplicate_fiscal_code_and_duplicate_code_are_refused()
    {
        var code = Code();
        var cui = NewFiscalCode();
        var saved = await Send(Legal(code, "Agro Nord Distribuție SRL", "RO" + cui, properties: await Segment()));
        var detail = await Send(new GetPartner(saved.Id));
        Assert.Equal((code, cui, (byte)1), (detail.Code, detail.FiscalCode, detail.PartnerType));

        var duplicateFiscal = await Assert.ThrowsAsync<ErpException>(async () => await Send(Legal(Code(), "Alt partener", cui, properties: await Segment())));
        Assert.Equal("CORE_PARTNER_DUPLICATE_FISCAL_CODE", duplicateFiscal.Code);

        var duplicateCode = await Assert.ThrowsAsync<ErpException>(async () =>
            await Send(Legal(code.ToLowerInvariant(), "Alt partener", NewFiscalCode(), properties: await Segment())));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicateCode.Code);

        var invalid = await Assert.ThrowsAsync<ErpValidationException>(() => Send(Legal(Code(), "Invalid", "RO12345679", supplier: true, customer: false)));
        Assert.Contains("fiscalCode", invalid.Errors.Keys);

        var noRole = await Assert.ThrowsAsync<ErpValidationException>(() => Send(Legal(Code(), "Fara rol", NewFiscalCode(), customer: false)));
        Assert.Contains("isCustomer", noRole.Errors.Keys);
    }

    [Fact]
    public async Task Empty_code_is_allocated_from_the_partner_series_of_the_current_company()
    {
        var year = (short)DateTime.UtcNow.Year;
        await Send(new SaveNumberSeries(null, database.CompanyId, null, "Core.Partner", "PART", year, 1, 5, false, true, true, null));

        var first = await Send(Legal(null, "Furnizor Ambalaje SRL", NewFiscalCode(), customer: false, supplier: true));
        var second = await Send(Legal("  ", "Furnizor Doi SRL", NewFiscalCode(), customer: false, supplier: true));
        Assert.Equal($"PART-{year}-00001", (await Send(new GetPartner(first.Id))).Code);
        Assert.Equal($"PART-{year}-00002", (await Send(new GetPartner(second.Id))).Code);

        var installation = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(Legal(null, "Fara firma", NewFiscalCode(), customer: false, supplier: true), companyId: 0));
        Assert.Contains("code", installation.Errors.Keys);
    }

    [Fact]
    [Trait("Criteriu", "02.01-A13")]
    public async Task Personal_id_is_encrypted_kept_on_update_and_revealed_only_with_permission_and_audited()
    {
        var create = new SavePartner(null, Code(), "Popescu Maria", 4, null, false, true, null, false, false, false, null, "Str. Florilor 3",
            "Iași", "IS", "RO", null, "0722 123 456", null, null, true, null, ChangePersonalId: true, PersonalId: Cnp);
        var saved = await Send(create);

        await using var connection = new SqlConnection(database.ConnectionString);
        var stored = await connection.ExecuteScalarAsync<byte[]>("SELECT PersonalIdEncrypted FROM core.Partner WHERE Id = @Id;", new { saved.Id });
        Assert.NotNull(stored);
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Cnp)));

        var detail = await Send(new GetPartner(saved.Id));
        Assert.True(detail.HasPersonalId);

        // Modificarea fara ChangePersonalId pastreaza CNP-ul.
        var updated = await Send(create with { Id = saved.Id, Name = "Popescu Maria Ioana", RowVersion = detail.RowVersion, PersonalId = null, ChangePersonalId = false });
        Assert.True((await Send(new GetPartner(updated.Id))).HasPersonalId);

        var forbidden = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new RevealPartnerPersonalId(saved.Id), without: CorePermissions.PartnerViewPersonalData));
        Assert.Equal(ErpErrorKind.Forbidden, forbidden.Kind);

        Assert.Equal(Cnp, (await Send(new RevealPartnerPersonalId(saved.Id))).PersonalId);

        var audit = (await connection.QueryAsync<(string Action, string? Changes)>(
            "SELECT Action, Changes FROM core.AuditEntry WHERE EntityType = N'Core.Partner' AND EntityId = @Id ORDER BY Id;", new { saved.Id })).ToList();
        Assert.Equal(["Core.Partner.Save", "Core.Partner.Save", "Core.Partner.ViewPersonalData"], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.DoesNotContain(Cnp, a.Changes ?? string.Empty, StringComparison.Ordinal));
        Assert.All(audit, a => Assert.DoesNotContain("Popescu", a.Changes ?? string.Empty, StringComparison.Ordinal));

        var invalidCnp = await Assert.ThrowsAsync<ErpValidationException>(() => Send(create with { Code = Code(), PersonalId = "1800101221145" }));
        Assert.Contains("personalId", invalidCnp.Errors.Keys);
    }

    [Fact]
    public async Task Stale_row_version_is_a_concurrency_conflict()
    {
        var saved = await Send(Legal(Code(), "Energo Serv SRL", NewFiscalCode(), properties: await Segment()));
        var detail = await Send(new GetPartner(saved.Id));
        var request = Legal(detail.Code, "Energo Serv SA", detail.FiscalCode!) with { Id = saved.Id, RowVersion = detail.RowVersion };
        await Send(request);

        var conflict = await Assert.ThrowsAsync<ErpException>(() => Send(request with { Name = "Altceva" }));
        Assert.Equal(CoreErrorCodes.ConcurrencyConflict, conflict.Code);
    }

    [Fact]
    public async Task Properties_are_validated_by_type_and_role_saved_and_filterable()
    {
        var segmentId = await PropertyId("SEGMENT");
        var missing = await Assert.ThrowsAsync<ErpValidationException>(() => Send(Legal(Code(), "Fara segment", NewFiscalCode(), properties: [])));
        Assert.Contains($"properties.{segmentId}", missing.Errors.Keys);

        // Segmentul obligatoriu se aplica doar clientilor: un furnizor se salveaza fara el.
        await Send(Legal(Code(), "Doar furnizor", NewFiscalCode(), customer: false, supplier: true, properties: []));

        var rating = await PropertyId("SUPPLIER_RATING");
        var badNumber = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(Legal(Code(), "Rating gresit", NewFiscalCode(), customer: false, supplier: true, properties: [new(rating, ValueNumber: "patru")])));
        Assert.Contains($"properties.{rating}", badNumber.Errors.Keys);

        var lookups = await Lookups();
        var days = lookups.Properties.Single(p => p.Code == "DELIVERY_DAYS").Id;
        var dayOptions = lookups.Options.Where(o => o.PropertyDefinitionId == days).Take(2).Select(o => o.Id).ToList();
        var horeca = (await Segment("HORECA")).Single();
        var saved = await Send(Legal(Code(), "Mega Retail SA", NewFiscalCode(), customer: true, supplier: true, properties:
        [
            horeca, new(days, ValueOptionId: dayOptions[0]), new(days, ValueOptionId: dayOptions[1]),
            new(rating, ValueNumber: "4"), new(await PropertyId("ISO_9001_UNTIL"), ValueDate: new DateOnly(2027, 6, 30)),
            new(await PropertyId("APPROVED"), ValueBool: true),
        ]));

        var detail = await Send(new GetPartner(saved.Id));
        Assert.Equal(6, detail.Properties.Count);
        Assert.Contains(detail.Properties, p => p.PropertyDefinitionId == rating && p.ValueNumber == "4");

        var filtered = await Send(new SearchPartners(PropertyDefinitionId: segmentId, PropertyOptionId: horeca.ValueOptionId));
        Assert.Contains(filtered.Items, i => i.Id == saved.Id);
        Assert.All(filtered.Items, i => Assert.True(i.IsCustomer));

        // Properties = null lasa valorile neschimbate; o lista goala pentru un furnizor le sterge.
        await Send(Legal(detail.Code, detail.Name, detail.FiscalCode!, customer: true, supplier: true) with { Id = saved.Id, RowVersion = detail.RowVersion });
        Assert.Equal(6, (await Send(new GetPartner(saved.Id))).Properties.Count);
    }

    [Fact]
    public async Task Search_ignores_case_and_diacritics_and_filters_by_role_and_type()
    {
        var prefix = "Z" + Guid.NewGuid().ToString("N")[..6];
        var customer = await Send(Legal(Code(), prefix + " Brașov Construcții SRL", NewFiscalCode(), properties: await Segment()));
        var supplier = await Send(Legal(Code(), prefix + " Brașov Furnizor SRL", NewFiscalCode(), customer: false, supplier: true));

        var all = await Send(new SearchPartners(prefix.ToLowerInvariant() + " brasov"));
        Assert.Equal(2, all.Total);
        Assert.True(all.ActiveCount >= 2);

        var suppliers = await Send(new SearchPartners(prefix, Role: 2));
        Assert.Equal([supplier.Id], suppliers.Items.Select(i => i.Id));

        var detail = await Send(new GetPartner(customer.Id));
        var byCui = await Send(new SearchPartners("RO" + detail.FiscalCode));
        Assert.Equal([customer.Id], byCui.Items.Select(i => i.Id));

        var page = await Send(new SearchPartners(prefix, PageSize: 1, Page: 2));
        Assert.Single(page.Items);
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task Addresses_bank_accounts_and_contacts_keep_one_default_and_appear_in_history()
    {
        var partner = await Send(Legal(Code(), "Transport Rapid SRL", NewFiscalCode(), properties: await Segment()));
        var types = (await Send(new GetOrgLookups())).AddressTypes;
        var delivery = types.Single(t => t.Code == "DELIVERY").Id;

        var first = await Send(new SavePartnerAddress(null, partner.Id, delivery, "Depozit", "Str. 1", "Cluj-Napoca", "CJ", "RO", null, null, null, true, true, null));
        await Send(new SavePartnerAddress(null, partner.Id, delivery, "Depozit 2", "Str. 2", "Cluj-Napoca", "CJ", "RO", null, null, null, true, true, null));
        var noCounty = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(new SavePartnerAddress(null, partner.Id, delivery, null, "Str. 3", "Cluj", null, "RO", null, null, null, false, true, null)));
        Assert.Contains("countyCode", noCounty.Errors.Keys);

        await Send(new SavePartnerBankAccount(null, partner.Id, "RO49 AAAA 1B31 0075 9384 0000", "Banca", "RON", true, true, null));
        var duplicateIban = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SavePartnerBankAccount(null, partner.Id, "RO49AAAA1B31007593840000", "Banca", "RON", false, true, null)));
        Assert.Equal("CORE_BANK_ACCOUNT_DUPLICATE_IBAN", duplicateIban.Code);

        await Send(new SavePartnerContact(null, partner.Id, "Ion Ionescu", "Achiziții", null, "ion@example.com", null, true, null));

        var detail = await Send(new GetPartner(partner.Id));
        Assert.Equal(1, detail.Addresses.Count(a => a.IsDefault));
        Assert.False(detail.Addresses.Single(a => a.Id == first.Id).IsDefault);
        Assert.Equal("RO49AAAA1B31007593840000", Assert.Single(detail.BankAccounts).Iban);
        Assert.Single(detail.Contacts);

        var history = await Send(new GetPartnerHistory(partner.Id));
        Assert.Equal(["Core.PartnerContact", "Core.PartnerBankAccount", "Core.PartnerAddress", "Core.PartnerAddress", "Core.Partner"],
            history.Select(h => h.EntityType));
        Assert.All(history, h => Assert.DoesNotContain("ion@example.com", h.Changes ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Partner_groups_have_unique_codes_and_are_used_by_partners()
    {
        var code = "G" + Guid.NewGuid().ToString("N")[..6];
        var group = await Send(new SavePartnerGroup(null, code, "Distribuitori", 10, true, null));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new SavePartnerGroup(null, code.ToLowerInvariant(), "Alta", 20, true, null)));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);

        var partner = await Send(Legal(Code(), "Office Line SRL", NewFiscalCode(), properties: await Segment()) with { PartnerGroupId = group.Id });
        Assert.Equal(group.Id, (await Send(new GetPartner(partner.Id))).PartnerGroupId);

        var lookups = await Lookups();
        var saved = lookups.Groups.Single(g => g.Id == group.Id);
        var inUse = await Assert.ThrowsAsync<ErpException>(() => Send(new SavePartnerGroup(group.Id, code, "Distribuitori", 10, false, saved.RowVersion)));
        Assert.Equal("CORE_ENTITY_IN_USE", inUse.Code);
    }
}
