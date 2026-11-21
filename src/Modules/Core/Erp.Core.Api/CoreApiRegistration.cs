using Erp.Core.Application.Audit;
using Erp.Core.Application.Dimensions;
using Erp.Core.Application.ExchangeRates;
using Erp.Core.Application.Licensing;
using Erp.Core.Application.Me;
using Erp.Core.Application.NumberSeries;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Application.Precision;
using Erp.Core.Application.Anaf;
using Erp.Core.Application.Settings;
using Erp.Core.Application.Properties;
using Erp.Core.Application.Items;
using Erp.Core.Application.Users;
using Erp.Core.Application.Employees;
using Erp.Core.Contracts;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Api;

public static class CoreApiRegistration
{
    /// <summary>Contextul cererii si ProblemDetails, comune tuturor modulelor.</summary>
    public static IServiceCollection AddErpApi(this IServiceCollection services)
    {
        services.AddScoped<HttpCurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<HttpCurrentContext>());
        services.AddScoped<ICurrentUserProfile>(sp => sp.GetRequiredService<HttpCurrentContext>());
        services.AddSingleton<IPersonalDataProtector, PersonalDataProtector>();
        services.AddProblemDetails();
        services.AddExceptionHandler<ErpExceptionHandler>();
        // Numerele JSON stricte: contractul OpenAPI are tipuri exacte; sumele si cantitatile circula ca siruri (UI, bani).
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
        return services;
    }

    /// <summary>Tratarea erorilor si contextul cererii; dupa autentificare, inainte de endpoint-uri.</summary>
    public static IApplicationBuilder UseErpApi(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<CurrentContextMiddleware>();
        return app;
    }

    /// <summary>Endpoint-urile Nucleului, subtiri (NET-03): cererea merge direct in MediatR.</summary>
    public static IEndpointRouteBuilder MapCoreApi(this IEndpointRouteBuilder endpoints)
    {
        var core = endpoints.MapGroup("/api/core").WithTags("Core");
        core.MapGet("/me", (ISender sender, CancellationToken ct) => sender.Send(new GetCurrentUser(), ct))
            .WithName("GetCurrentUser")
            .Produces<CurrentUserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        MapOrganization(core);
        MapPartners(core);
        MapSettings(core);
        MapItems(core);
        MapUsers(core);
        MapRoles(core);
        MapExchangeRates(core);
        MapDimensions(core);
        MapPrecision(core);
        MapAudit(core);
        MapEmployees(core);
        MapLicense(core);
        return endpoints;
    }

    // Administrare › Configurare › Licenta (08, 4.1; macheta 07j din 02.01). Fisierul .lic vine in Base64 (cateva sute de octeti).
    private static void MapLicense(RouteGroupBuilder core)
    {
        core.MapGet("/license", (ISender sender, CancellationToken ct) => sender.Send(new GetLicense(), ct))
            .WithName("GetLicense").Produces<LicenseDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/license", (UploadLicense body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("UploadLicense").Produces<LicenseDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    // Structura organizatorica (macheta 03) si seriile de numerotare (macheta 07c). Antetul X-Company-Id alege firma pe
    // care se verifica permisiunile; 0 = nivelul instalarii (grupuri si firme noi).
    private static void MapOrganization(RouteGroupBuilder core)
    {
        core.MapGet("/org/tree", (ISender sender, CancellationToken ct) => sender.Send(new GetOrgTree(), ct))
            .WithName("GetOrgTree").Produces<OrgTreeDto>();
        core.MapGet("/org/lookups", (ISender sender, CancellationToken ct) => sender.Send(new GetOrgLookups(), ct))
            .WithName("GetOrgLookups").Produces<OrgLookupsDto>();

        core.MapPost("/groups", (SaveCompanyGroup body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateCompanyGroup").Saves();
        core.MapPut("/groups/{id:int}", (int id, SaveCompanyGroup body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateCompanyGroup").Saves();

        core.MapGet("/companies/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetCompany(id), ct))
            .WithName("GetCompany").Produces<CompanyDetailDto>().ProducesProblem(StatusCodes.Status404NotFound);
        core.MapPost("/companies", (SaveCompany body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateCompany").Saves();
        core.MapPut("/companies/{id:int}", (int id, SaveCompany body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateCompany").Saves();

        core.MapPost("/companies/{companyId:int}/branches", (int companyId, SaveBranch body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, CompanyId = companyId }, ct))
            .WithName("CreateBranch").Saves();
        core.MapPut("/companies/{companyId:int}/branches/{id:int}", (int companyId, int id, SaveBranch body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id, CompanyId = companyId }, ct))
            .WithName("UpdateBranch").Saves();

        core.MapPost("/companies/{companyId:int}/locations", (int companyId, SaveLocation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, CompanyId = companyId }, ct))
            .WithName("CreateLocation").Saves();
        core.MapPut("/companies/{companyId:int}/locations/{id:int}", (int companyId, int id, SaveLocation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id, CompanyId = companyId }, ct))
            .WithName("UpdateLocation").Saves();

        core.MapPost("/companies/{companyId:int}/bank-accounts", (int companyId, SaveCompanyBankAccount body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, CompanyId = companyId }, ct))
            .WithName("CreateCompanyBankAccount").Saves();
        core.MapPut("/companies/{companyId:int}/bank-accounts/{id:int}",
                (int companyId, int id, SaveCompanyBankAccount body, ISender sender, CancellationToken ct) =>
                    sender.Send(body with { Id = id, CompanyId = companyId }, ct))
            .WithName("UpdateCompanyBankAccount").Saves();

        core.MapPost("/companies/{companyId:int}/addresses", (int companyId, SaveCompanyAddress body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, CompanyId = companyId }, ct))
            .WithName("CreateCompanyAddress").Saves();
        core.MapPut("/companies/{companyId:int}/addresses/{id:int}",
                (int companyId, int id, SaveCompanyAddress body, ISender sender, CancellationToken ct) =>
                    sender.Send(body with { Id = id, CompanyId = companyId }, ct))
            .WithName("UpdateCompanyAddress").Saves();

        core.MapPost("/companies/{companyId:int}/contacts", (int companyId, SaveCompanyContact body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, CompanyId = companyId }, ct))
            .WithName("CreateCompanyContact").Saves();
        core.MapPut("/companies/{companyId:int}/contacts/{id:int}",
                (int companyId, int id, SaveCompanyContact body, ISender sender, CancellationToken ct) =>
                    sender.Send(body with { Id = id, CompanyId = companyId }, ct))
            .WithName("UpdateCompanyContact").Saves();

        core.MapPost("/address-types", (SaveAddressType body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateAddressType").Saves();
        core.MapPut("/address-types/{id:int}", (int id, SaveAddressType body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateAddressType").Saves();

        core.MapGet("/companies/{companyId:int}/number-series", (int companyId, short? year, ISender sender, CancellationToken ct) =>
                sender.Send(new ListNumberSeries(companyId, year), ct))
            .WithName("ListNumberSeries").Produces<IReadOnlyList<NumberSeriesDto>>();
        core.MapPost("/companies/{companyId:int}/number-series", (int companyId, SaveNumberSeries body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, CompanyId = companyId }, ct))
            .WithName("CreateNumberSeries").Saves();
        core.MapPut("/companies/{companyId:int}/number-series/{id:int}",
                (int companyId, int id, SaveNumberSeries body, ISender sender, CancellationToken ct) =>
                    sender.Send(body with { Id = id, CompanyId = companyId }, ct))
            .WithName("UpdateNumberSeries").Saves();
        core.MapPost("/companies/{companyId:int}/number-series/{id:int}/close",
                (int companyId, int id, CloseNumberSeriesBody body, ISender sender, CancellationToken ct) =>
                    sender.Send(new CloseNumberSeries(id, companyId, body.RowVersion), ct))
            .WithName("CloseNumberSeries").Saves();
    }

    // Configurare › Setari (macheta 07f) si interogarea ANAF a unui CUI („Preia de la ANAF” pe fisa partenerului).
    private static void MapSettings(RouteGroupBuilder core)
    {
        core.MapGet("/settings", (ISender sender, CancellationToken ct) => sender.Send(new GetInstallationSettings(), ct))
            .WithName("GetInstallationSettings").Produces<InstallationSettingsDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPut("/settings", (SaveInstallationSettings body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveInstallationSettings").Produces<InstallationSettingsDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        core.MapGet("/property-definitions", (string entityType, ISender sender, CancellationToken ct) => sender.Send(new ListPropertyDefinitions(entityType), ct))
            .WithName("ListPropertyDefinitions").Produces<IReadOnlyList<PropertyDefinitionAdminDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/property-definitions", (SavePropertyDefinition body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreatePropertyDefinition").Saves();
        core.MapPut("/property-definitions/{id:int}", (int id, SavePropertyDefinition body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdatePropertyDefinition").Saves();
        MapImport(core);
        core.MapGet("/anaf/companies/{fiscalCode}", (string fiscalCode, ISender sender, CancellationToken ct) => sender.Send(new LookupAnafCompany(fiscalCode), ct))
            .WithName("LookupAnafCompany").Produces<AnafCompanyDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    // Parteneri (macheta 04): nomenclator la nivelul instalarii; permisiunile se verifica pe firma curenta (X-Company-Id).
    private static void MapPartners(RouteGroupBuilder core)
    {
        // Proprietarii posibili (comun, firma curenta, punctele ei de lucru si locatiile; 21.11.2026) pentru Item sau Partner.
        core.MapGet("/owner-options/{entity}", (string entity, ISender sender, CancellationToken ct) => sender.Send(new GetOwnerOptions(entity), ct))
            .WithName("GetOwnerOptions").Produces<OwnerOptionsDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/partners", ([AsParameters] SearchPartners query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchPartners").Produces<PartnerPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/partners/export", async ([AsParameters] ExportPartners query, ISender sender, CancellationToken ct) =>
                File(await sender.Send(query, ct)))
            .WithName("ExportPartners").Produces<byte[]>(StatusCodes.Status200OK, FileDto.Xlsx).ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/partners/lookups", (ISender sender, CancellationToken ct) => sender.Send(new GetPartnerLookups(), ct))
            .WithName("GetPartnerLookups").Produces<PartnerLookupsDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/partners/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPartner(id), ct))
            .WithName("GetPartner").Produces<PartnerDetailDto>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        core.MapGet("/partners/{id:int}/history", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPartnerHistory(id), ct))
            .WithName("GetPartnerHistory").Produces<IReadOnlyList<PartnerHistoryEntryDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/partners/{id:int}/personal-id", (int id, ISender sender, CancellationToken ct) => sender.Send(new RevealPartnerPersonalId(id), ct))
            .WithName("RevealPartnerPersonalId").Produces<PartnerPersonalIdDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        core.MapPost("/partners", (SavePartner body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreatePartner").Saves();
        core.MapPut("/partners/{id:int}", (int id, SavePartner body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdatePartner").Saves();

        core.MapPost("/partners/{partnerId:int}/addresses", (int partnerId, SavePartnerAddress body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, PartnerId = partnerId }, ct))
            .WithName("CreatePartnerAddress").Saves();
        core.MapPut("/partners/{partnerId:int}/addresses/{id:int}", (int partnerId, int id, SavePartnerAddress body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id, PartnerId = partnerId }, ct))
            .WithName("UpdatePartnerAddress").Saves();

        core.MapPost("/partners/{partnerId:int}/bank-accounts", (int partnerId, SavePartnerBankAccount body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, PartnerId = partnerId }, ct))
            .WithName("CreatePartnerBankAccount").Saves();
        core.MapPut("/partners/{partnerId:int}/bank-accounts/{id:int}",
                (int partnerId, int id, SavePartnerBankAccount body, ISender sender, CancellationToken ct) =>
                    sender.Send(body with { Id = id, PartnerId = partnerId }, ct))
            .WithName("UpdatePartnerBankAccount").Saves();

        core.MapPost("/partners/{partnerId:int}/contacts", (int partnerId, SavePartnerContact body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, PartnerId = partnerId }, ct))
            .WithName("CreatePartnerContact").Saves();
        core.MapPut("/partners/{partnerId:int}/contacts/{id:int}", (int partnerId, int id, SavePartnerContact body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id, PartnerId = partnerId }, ct))
            .WithName("UpdatePartnerContact").Saves();

        core.MapPost("/partner-groups", (SavePartnerGroup body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreatePartnerGroup").Saves();
        core.MapPut("/partner-groups/{id:int}", (int id, SavePartnerGroup body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdatePartnerGroup").Saves();
    }

    // Articole (macheta 05/05b) si nomenclatoarele lor (Configurare › Nomenclatoare, macheta 07).
    private static void MapItems(RouteGroupBuilder core)
    {
        core.MapGet("/items", ([AsParameters] SearchItems query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchItems").Produces<ItemPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/items/export", async ([AsParameters] ExportItems query, ISender sender, CancellationToken ct) => File(await sender.Send(query, ct)))
            .WithName("ExportItems").Produces<byte[]>(StatusCodes.Status200OK, FileDto.Xlsx).ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/items/lookups", (ISender sender, CancellationToken ct) => sender.Send(new GetItemLookups(), ct))
            .WithName("GetItemLookups").Produces<ItemLookupsDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/items/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetItem(id), ct))
            .WithName("GetItem").Produces<ItemDetailDto>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        core.MapGet("/items/{id:int}/history", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetItemHistory(id), ct))
            .WithName("GetItemHistory").Produces<IReadOnlyList<PartnerHistoryEntryDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/items", (SaveItem body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateItem").Saves();
        core.MapPut("/items/{id:int}", (int id, SaveItem body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateItem").Saves();
        core.MapPost("/items/{itemId:int}/barcodes", (int itemId, SaveItemBarcode body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, ItemId = itemId }, ct))
            .WithName("CreateItemBarcode").Saves();
        core.MapPut("/items/{itemId:int}/barcodes/{id:int}", (int itemId, int id, SaveItemBarcode body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id, ItemId = itemId }, ct))
            .WithName("UpdateItemBarcode").Saves();

        core.MapPut("/vat-rates", (SaveVatRates body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveVatRates").Produces<int>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        core.MapPut("/units-of-measure", (SaveUnitsOfMeasure body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveUnitsOfMeasure").Produces<int>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        core.MapPost("/uom-sets", (SaveUomSet body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateUomSet").Saves();
        core.MapPut("/uom-sets/{id:int}", (int id, SaveUomSet body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateUomSet").Saves();
        core.MapPost("/item-groups", (SaveItemGroup body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateItemGroup").Saves();
        core.MapPut("/item-groups/{id:int}", (int id, SaveItemGroup body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateItemGroup").Saves();
    }

    // Utilizatori (machetele 06, 06b, 06c): conturile din Nucleu si cele de autentificare; Core.User.Manage la nivelul instalarii.
    private static void MapUsers(RouteGroupBuilder core)
    {
        core.MapGet("/users", ([AsParameters] SearchUsers query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchUsers").Produces<UserPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/users/lookups", (ISender sender, CancellationToken ct) => sender.Send(new GetUserLookups(), ct))
            .WithName("GetUserLookups").Produces<UserLookupsDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/users/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetUser(id), ct))
            .WithName("GetUser").Produces<UserDetailDto>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        core.MapGet("/users/{id:int}/history", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetUserHistory(id), ct))
            .WithName("GetUserHistory").Produces<IReadOnlyList<PartnerHistoryEntryDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/users/{id:int}/sessions", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetUserSessions(id), ct))
            .WithName("GetUserSessions").Produces<IReadOnlyList<IdentityAccountSession>>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        core.MapPost("/users", (SaveUser body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateUser").SavesUser();
        core.MapPut("/users/{id:int}", (int id, SaveUser body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateUser").SavesUser();
        core.MapPost("/users/{id:int}/reset-password", (int id, ISender sender, CancellationToken ct) => sender.Send(new ResetUserPassword(id), ct))
            .WithName("ResetUserPassword").Produces<TemporaryPasswordDto>().SecurityAction();
        core.MapPost("/users/{id:int}/unlock", (int id, ISender sender, CancellationToken ct) => sender.Send(new UnlockUser(id), ct))
            .WithName("UnlockUser").Produces<bool>().SecurityAction();
        core.MapPost("/users/{id:int}/reset-two-factor", (int id, ISender sender, CancellationToken ct) => sender.Send(new ResetUserTwoFactor(id), ct))
            .WithName("ResetUserTwoFactor").Produces<bool>().SecurityAction();
        core.MapPost("/users/{id:int}/revoke-sessions", (int id, RevokeUserSessionsBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new RevokeUserSessions(id, body.SessionId), ct))
            .WithName("RevokeUserSessions").Produces<bool>().SecurityAction();
    }

    // Configurare › Precizie (02.01, 3.5; macheta 07d): politicile de zecimale si precizia unui articol pe firma curenta.
    private static void MapPrecision(RouteGroupBuilder core)
    {
        core.MapGet("/precision-policies", (ISender sender, CancellationToken ct) => sender.Send(new GetPrecisionPolicies(), ct))
            .WithName("GetPrecisionPolicies").Produces<PrecisionPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/precision", ([AsParameters] ResolvePrecision query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("ResolvePrecision").Produces<PrecisionInfo>().ProducesValidationProblem();
        core.MapPost("/precision-policies", (SavePrecisionPolicy body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreatePrecisionPolicy").Saves();
        core.MapPut("/precision-policies/{id:int}", (int id, SavePrecisionPolicy body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdatePrecisionPolicy").Saves();
    }

    // Configurare › Audit (02.01, 11; macheta 07h): jurnalul de audit si de autentificari, doar citire; Istoricul fisei firmei.
    private static void MapAudit(RouteGroupBuilder core)
    {
        core.MapGet("/audit", ([AsParameters] SearchAudit query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchAudit").Produces<AuditPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/audit/{id:long}", (long id, ISender sender, CancellationToken ct) => sender.Send(new GetAuditEntry(id), ct))
            .WithName("GetAuditEntry").Produces<AuditEntryDetailDto>().ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        core.MapGet("/audit/logins", ([AsParameters] SearchLoginEvents query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchLoginEvents").Produces<LoginEventPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/companies/{id:int}/history", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetCompanyHistory(id), ct))
            .WithName("GetCompanyHistory").Produces<IReadOnlyList<PartnerHistoryEntryDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
    }

    // Configurare › Dimensiuni (02.01, 3.5; macheta 07k): centre de cost, proiecte, gestiuni, santiere.
    private static void MapDimensions(RouteGroupBuilder core)
    {
        core.MapGet("/dimensions", ([AsParameters] GetDimensions query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetDimensions").Produces<DimensionPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/dimensions", (SaveDimensionValue body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateDimensionValue").Saves();
        core.MapPut("/dimensions/{id:int}", (int id, SaveDimensionValue body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateDimensionValue").Saves();
    }

    // Configurare › Cursuri valutare (02.01, 4.3; macheta 07b): valutele, cursurile BNR (doar citire) si cele manuale.
    private static void MapExchangeRates(RouteGroupBuilder core)
    {
        core.MapGet("/exchange-rates", ([AsParameters] GetExchangeRates query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetExchangeRates").Produces<ExchangeRatesDto>().ProducesValidationProblem();
        core.MapGet("/currencies", (ISender sender, CancellationToken ct) => sender.Send(new GetCurrencies(), ct))
            .WithName("GetCurrencies").Produces<IReadOnlyList<CurrencyDto>>();
        core.MapGet("/exchange-rates/rate", ([AsParameters] GetExchangeRate query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetExchangeRate").Produces<ExchangeRateDto>().ProducesProblem(StatusCodes.Status409Conflict);
        core.MapPost("/exchange-rates/manual", (SaveManualExchangeRate body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateManualExchangeRate").Saves();
        core.MapPut("/exchange-rates/manual/{id:int}", (int id, SaveManualExchangeRate body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdateManualExchangeRate").Saves();
        core.MapPost("/exchange-rates/import-bnr", (ISender sender, CancellationToken ct) => sender.Send(new ImportBnrRates(), ct))
            .WithName("ImportBnrRates").Produces<BnrImportResultDto>().ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        core.MapPut("/currencies/{code}/active", (string code, SetCurrencyActiveBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new SetCurrencyActive(code, body.IsActive, body.RowVersion), ct))
            .WithName("SetCurrencyActive").Produces<byte[]>().ProducesValidationProblem().SecurityAction();
    }

    // Configurare › Roluri (02.01, 8; macheta 07e si vederea „Roluri și utilizatori”): Core.Role.Manage.
    private static void MapRoles(RouteGroupBuilder core)
    {
        core.MapGet("/roles/matrix", ([AsParameters] GetRoleMatrix query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetRoleMatrix").Produces<RoleMatrixDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/companies/{id:int}/access", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetCompanyAccess(id), ct))
            .WithName("GetCompanyAccess").Produces<RoleMatrixDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/roles", (SaveRole body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateRole").Saves();
        core.MapPut("/roles/{id:int}", (int id, SaveRole body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateRole").Saves();
        core.MapPost("/roles/{id:int}/delete", (int id, DeleteRoleBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new DeleteRole(id, body.RowVersion), ct))
            .WithName("DeleteRole").Produces<bool>().SecurityAction();
        core.MapPut("/users/{id:int}/permission-overrides", (int id, SaveUserPermissionOverridesBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new SaveUserPermissionOverrides(id, body.CompanyId, body.Overrides), ct))
            .WithName("SaveUserPermissionOverrides").Produces<bool>().ProducesValidationProblem().SecurityAction();
    }

    // Angajati: fisa minima din Nucleu (02.01, 3.4), de care se leaga utilizatorii angajati; modulul HR o extinde.
    private static void MapEmployees(RouteGroupBuilder core)
    {
        core.MapGet("/employees", ([AsParameters] SearchEmployees query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchEmployees").Produces<EmployeePageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/employees/lookups", (ISender sender, CancellationToken ct) => sender.Send(new GetEmployeeLookups(), ct))
            .WithName("GetEmployeeLookups").Produces<EmployeeLookupsDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/employees/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetEmployee(id), ct))
            .WithName("GetEmployee").Produces<EmployeeDetailDto>().ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        core.MapGet("/employees/{id:int}/history", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetEmployeeHistory(id), ct))
            .WithName("GetEmployeeHistory").Produces<IReadOnlyList<PartnerHistoryEntryDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/employees", (SaveEmployee body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateEmployee").Saves();
        core.MapPut("/employees/{id:int}", (int id, SaveEmployee body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateEmployee").Saves();
    }

    // Configurare › Import (macheta 07g; fluxul 02.01, 4.8): sablon, incarcare si validare, confirmare in loturi, randuri cu erori.
    private static void MapImport(RouteGroupBuilder core)
    {
        core.MapGet("/import/partners/template", async (ISender sender, CancellationToken ct) => File(await sender.Send(new GetPartnerImportTemplate(), ct)))
            .WithName("GetPartnerImportTemplate").Produces<byte[]>(StatusCodes.Status200OK, FileDto.Xlsx).ProducesProblem(StatusCodes.Status403Forbidden);

        // Analiza (cu verificarea ANAF optionala) ruleaza in afara tranzactiei; jobul se salveaza apoi, in tranzactia lui.
        core.MapPost("/import/partners", async (UploadPartnerImport body, ISender sender, CancellationToken ct) =>
            {
                var analysis = await sender.Send(new AnalyzePartnerImport(body.FileName, body.FileBase64, body.AnafCheck), ct);
                return await sender.Send(new CreatePartnerImportJob(analysis), ct);
            })
            .WithName("UploadPartnerImport").Produces<ImportJobDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        core.MapGet("/import/items/template", async (ISender sender, CancellationToken ct) => File(await sender.Send(new GetItemImportTemplate(), ct)))
            .WithName("GetItemImportTemplate").Produces<byte[]>(StatusCodes.Status200OK, FileDto.Xlsx).ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapPost("/import/items", async (UploadItemImport body, ISender sender, CancellationToken ct) =>
            {
                var analysis = await sender.Send(new AnalyzeItemImport(body.FileName, body.FileBase64), ct);
                return await sender.Send(new CreateItemImportJob(analysis), ct);
            })
            .WithName("UploadItemImport").Produces<ImportJobDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Confirmarea: loturi de cate 500 de randuri, fiecare in tranzactia lui (4.8 pasul 4). Un lot esuat lasa importul partial.
        core.MapPost("/import/{id:int}/confirm", async (int id, ISender sender, CancellationToken ct) =>
            {
                try
                {
                    var entityType = (await sender.Send(new GetImportJob(id), ct)).EntityType;
                    while (!(entityType == "Item"
                        ? await sender.Send(new ImportItemBatch(id), ct)
                        : await sender.Send(new ImportPartnerBatch(id), ct)).Done)
                    {
                    }
                }
                catch (ErpException ex) when (ex.Code is not ("CORE_IMPORT_NOT_PENDING" or "CORE_ENTITY_NOT_FOUND" or CoreErrorCodes.Forbidden))
                {
                    return await sender.Send(new FailImportJob(id, ex.Message), CancellationToken.None);
                }

                return await sender.Send(new GetImportJob(id), ct);
            })
            .WithName("ConfirmImport").Produces<ImportJobDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        core.MapPost("/import/{id:int}/cancel", (int id, ISender sender, CancellationToken ct) => sender.Send(new CancelImportJob(id), ct))
            .WithName("CancelImport").Produces<ImportJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        core.MapGet("/import/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetImportJob(id), ct))
            .WithName("GetImport").Produces<ImportJobDto>().ProducesProblem(StatusCodes.Status404NotFound);
        core.MapGet("/import", (string? entityType, ISender sender, CancellationToken ct) => sender.Send(new ListImportJobs(entityType), ct))
            .WithName("ListImports").Produces<IReadOnlyList<ImportJobDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        core.MapGet("/import/{id:int}/errors", async (int id, ISender sender, CancellationToken ct) => File(await sender.Send(new DownloadImportErrors(id), ct)))
            .WithName("DownloadImportErrors").Produces<byte[]>(StatusCodes.Status200OK, FileDto.Xlsx).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult File(FileDto file) => Results.File(file.Content, file.ContentType, file.FileName);

    private static RouteHandlerBuilder SavesUser(this RouteHandlerBuilder builder) => builder
        .Produces<UserSavedDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    private static RouteHandlerBuilder SecurityAction(this RouteHandlerBuilder builder) => builder
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    private static RouteHandlerBuilder Saves(this RouteHandlerBuilder builder) => builder
        .Produces<SavedEntity>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary>Fisierul de import: numele si continutul .xlsx in Base64; AnafCheck = completarea datelor dupa CUI de la ANAF.</summary>
public sealed record UploadPartnerImport(string FileName, string FileBase64, bool AnafCheck);

/// <summary>Fisierul de import articole: numele si continutul .xlsx in Base64.</summary>
public sealed record UploadItemImport(string FileName, string FileBase64);

/// <summary>Corpul cererii de inchidere a unei serii: versiunea vazuta de utilizator.</summary>
public sealed record CloseNumberSeriesBody(byte[] RowVersion);

/// <summary>Corpul cererii de inchidere a sesiunilor unui utilizator: o sesiune anume sau toate (null).</summary>
public sealed record RevokeUserSessionsBody(Guid? SessionId);
