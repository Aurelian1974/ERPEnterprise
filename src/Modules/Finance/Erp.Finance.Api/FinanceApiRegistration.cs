using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Finance.Api;

public static class FinanceApiRegistration
{
    /// <summary>Endpoint-urile Finantelor, subtiri (NET-03): cererea merge direct in MediatR; firma e cea curenta (antetul cererii).</summary>
    public static IEndpointRouteBuilder MapFinanceApi(this IEndpointRouteBuilder endpoints)
    {
        var finance = endpoints.MapGroup("/api/finance").WithTags("Finance");
        MapTreasuryAccounts(finance);
        return endpoints;
    }

    // Trezorerie › Conturi de trezorerie (02.02, F1 3.1; macheta f1-01).
    private static void MapTreasuryAccounts(RouteGroupBuilder finance)
    {
        finance.MapGet("/treasury-accounts", ([AsParameters] GetTreasuryAccounts query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetTreasuryAccounts").Produces<TreasuryAccountPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        finance.MapGet("/treasury-accounts/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetTreasuryAccount(id), ct))
            .WithName("GetTreasuryAccount").Produces<TreasuryAccountDetailDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        finance.MapPost("/treasury-accounts", (SaveTreasuryAccount body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateTreasuryAccount").Saves();
        finance.MapPut("/treasury-accounts/{id:int}", (int id, SaveTreasuryAccount body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdateTreasuryAccount").Saves();
    }

    private static RouteHandlerBuilder Saves(this RouteHandlerBuilder builder) => builder
        .Produces<FinanceSaved>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
}
