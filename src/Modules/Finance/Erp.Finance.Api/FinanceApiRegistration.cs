using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Api;

public static class FinanceApiRegistration
{
    /// <summary>Serviciile HTTP ale Finantelor: criptarea actului de identitate cu Data Protection-ul gazdei.</summary>
    public static IServiceCollection AddFinanceApi(this IServiceCollection services)
    {
        services.AddSingleton<IPersonIdProtector, PersonIdProtector>();
        return services;
    }

    /// <summary>Endpoint-urile Finantelor, subtiri (NET-03): cererea merge direct in MediatR; firma e cea curenta (antetul cererii).</summary>
    public static IEndpointRouteBuilder MapFinanceApi(this IEndpointRouteBuilder endpoints)
    {
        var finance = endpoints.MapGroup("/api/finance").WithTags("Finance");
        MapTreasuryAccounts(finance);
        MapCash(finance);
        MapCashRegister(finance);
        MapAdvances(finance);
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

    // Trezorerie › Casa si documentele de casa (02.02, F1 3.3 si 4.1; machetele f1-02, f1-03).
    private static void MapCash(RouteGroupBuilder finance)
    {
        finance.MapGet("/cash-desk", ([AsParameters] GetCashDesk query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetCashDesk").Produces<CashDeskDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        finance.MapGet("/cash-documents/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetCashDocument(id), ct))
            .WithName("GetCashDocument").Produces<CashDocumentDetailDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        finance.MapPost("/cash-documents", (SaveCashDocument body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateCashDocument").Saves();
        finance.MapPut("/cash-documents/{id:int}", (int id, SaveCashDocument body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdateCashDocument").Saves();
        finance.MapPost("/cash-documents/{id:int}/delete", (int id, RowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new DeleteCashDocument(id, body.RowVersion), ct))
            .WithName("DeleteCashDocument").Produces<int>().Problems();
        finance.MapPost("/cash-documents/{id:int}/validate", (int id, ValidateCashBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ValidateCashDocument(id, body.RowVersion, body.ConfirmWarnings), ct))
            .WithName("ValidateCashDocument").Produces<CashValidatedDto>().Problems();
        finance.MapPost("/cash-documents/{id:int}/reverse", (int id, RowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ReverseCashDocument(id, body.RowVersion), ct))
            .WithName("ReverseCashDocument").Produces<CashValidatedDto>().Problems();
    }

    // Trezorerie › Registrul de casa: inchiderea si redeschiderea zilei (02.02, F1 3.4 si 4.2; macheta f1-04).
    private static void MapCashRegister(RouteGroupBuilder finance)
    {
        finance.MapGet("/cash-register", ([AsParameters] GetCashRegister query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetCashRegister").Produces<CashRegisterDto>().Problems();
        finance.MapPost("/cash-days/close", (CloseCashDay body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("CloseCashDay").Produces<CashDayClosedDto>().Problems();
        finance.MapPost("/cash-days/reopen", (ReopenCashDay body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("ReopenCashDay").Produces<IReadOnlyList<DateOnly>>().Problems();
    }

    // Trezorerie › Avansuri spre decontare si deconturi de cheltuieli (02.02, F1 3.5, 3.7, 4.4; machetele f1-05, f1-06).
    private static void MapAdvances(RouteGroupBuilder finance)
    {
        finance.MapGet("/cash-advances", ([AsParameters] GetCashAdvances query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetCashAdvances").Produces<CashAdvancePageDto>().Problems();
        finance.MapPost("/cash-advances", (CreateCashAdvance body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("CreateCashAdvance").Produces<CashAdvanceCreatedDto>().Problems();
        finance.MapGet("/expense-reports", (ISender sender, CancellationToken ct) => sender.Send(new GetExpenseReports(), ct))
            .WithName("GetExpenseReports").Produces<ExpenseReportPageDto>().Problems();
        finance.MapGet("/expense-reports/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetExpenseReport(id), ct))
            .WithName("GetExpenseReport").Produces<ExpenseReportDetailDto>().Problems();
        finance.MapPost("/expense-reports", (SaveExpenseReport body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateExpenseReport").Saves();
        finance.MapPut("/expense-reports/{id:int}", (int id, SaveExpenseReport body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdateExpenseReport").Saves();
        finance.MapPost("/expense-reports/{id:int}/submit", (int id, RowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new SubmitExpenseReport(id, body.RowVersion), ct))
            .WithName("SubmitExpenseReport").Saves();
        finance.MapPost("/expense-reports/{id:int}/approve", (int id, RowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ApproveExpenseReport(id, body.RowVersion), ct))
            .WithName("ApproveExpenseReport").Saves();
        finance.MapPost("/expense-reports/{id:int}/validate", (int id, ValidateExpenseReportBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ValidateExpenseReport(id, body.RowVersion, body.SettlementTreasuryAccountId), ct))
            .WithName("ValidateExpenseReport").Produces<ExpenseReportValidatedDto>().Problems();
    }

    private static RouteHandlerBuilder Problems(this RouteHandlerBuilder builder) => builder
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    private static RouteHandlerBuilder Saves(this RouteHandlerBuilder builder) => builder
        .Produces<FinanceSaved>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary>Corpul unei actiuni pe document: versiunea vazuta de utilizator.</summary>
public sealed record RowVersionBody(byte[] RowVersion);

/// <summary>Corpul validarii unui document de casa: versiunea si confirmarea avertizarilor de plafon.</summary>
public sealed record ValidateCashBody(byte[] RowVersion, bool ConfirmWarnings);

/// <summary>Corpul validarii decontului: versiunea si casa in care se inchide diferenta.</summary>
public sealed record ValidateExpenseReportBody(byte[] RowVersion, int? SettlementTreasuryAccountId);
