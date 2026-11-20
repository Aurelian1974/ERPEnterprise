using Erp.Core.Application.Licensing;
using Erp.Core.Application.Me;
using Erp.Core.Application.NumberSeries;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
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

    // Parteneri (macheta 04): nomenclator la nivelul instalarii; permisiunile se verifica pe firma curenta (X-Company-Id).
    private static void MapPartners(RouteGroupBuilder core)
    {
        core.MapGet("/partners", ([AsParameters] SearchPartners query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchPartners").Produces<PartnerPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
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

    private static RouteHandlerBuilder Saves(this RouteHandlerBuilder builder) => builder
        .Produces<SavedEntity>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary>Corpul cererii de inchidere a unei serii: versiunea vazuta de utilizator.</summary>
public sealed record CloseNumberSeriesBody(byte[] RowVersion);
