using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
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
        MapTreasurySettings(finance);
        MapBank(finance);
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
        finance.MapGet("/treasury-accounts/{id:int}/history", (int id, ISender sender, CancellationToken ct) =>
                sender.Send(new GetTreasuryAccountHistory(id), ct))
            .WithName("GetTreasuryAccountHistory").Produces<IReadOnlyList<FinanceHistoryEntryDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
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

    // Configurare › Finante › Trezorerie (02.02, F1 3.2, 3.6, 3.7; machetele cf-01, cf-02, cf-03).
    private static void MapTreasurySettings(RouteGroupBuilder finance)
    {
        finance.MapGet("/operation-types", (ISender sender, CancellationToken ct) => sender.Send(new GetOperationTypes(), ct))
            .WithName("GetOperationTypes").Produces<IReadOnlyList<OperationTypeDto>>().Problems();
        finance.MapPost("/operation-types", (SaveOperationType body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateOperationType").Saves();
        finance.MapPut("/operation-types/{id:int}", (int id, SaveOperationType body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateOperationType").Saves();
        finance.MapGet("/expense-types", (ISender sender, CancellationToken ct) => sender.Send(new GetExpenseTypes(), ct))
            .WithName("GetExpenseTypes").Produces<IReadOnlyList<ExpenseTypeDto>>().Problems();
        finance.MapPost("/expense-types", (SaveExpenseType body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateExpenseType").Saves();
        finance.MapPut("/expense-types/{id:int}", (int id, SaveExpenseType body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateExpenseType").Saves();
        finance.MapGet("/cash-limits", (ISender sender, CancellationToken ct) => sender.Send(new GetCashLimitRules(), ct))
            .WithName("GetCashLimitRules").Produces<IReadOnlyList<CashLimitRuleDto>>().Problems();
        finance.MapPost("/cash-limits", (SaveCashLimitRule body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateCashLimitRule").Saves();
        finance.MapPut("/cash-limits/{id:int}", (int id, SaveCashLimitRule body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateCashLimitRule").Saves();
    }

    // Extrase bancare si profile de format (02.02, F2 3.1, 3.3, 4.1; machetele f2-01, f2-02, cf-04).
    private static void MapBank(RouteGroupBuilder finance)
    {
        finance.MapGet("/bank-statements", ([AsParameters] GetBankStatements query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetBankStatements").Produces<IReadOnlyList<BankStatementItemDto>>().Problems();
        finance.MapGet("/bank-statements/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetBankStatement(id), ct))
            .WithName("GetBankStatement").Produces<BankStatementDetailDto>().Problems();
        finance.MapPost("/bank-statements/import", (ImportBankStatement body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("ImportBankStatement").Produces<BankImportResultDto>().Problems();
        finance.MapPost("/bank-statements/{id:int}/accept-gap", (int id, AcceptGapBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new AcceptStatementGap(id, body.Reason, body.RowVersion), ct))
            .WithName("AcceptStatementGap").Produces<byte[]>().Problems();
        finance.MapGet("/bank-format-profiles", (ISender sender, CancellationToken ct) => sender.Send(new GetBankFormatProfiles(), ct))
            .WithName("GetBankFormatProfiles").Produces<IReadOnlyList<BankFormatProfileDto>>().Problems();
        finance.MapPost("/bank-format-profiles", (SaveBankFormatProfile body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateBankFormatProfile").Saves();
        finance.MapPut("/bank-format-profiles/{id:int}", (int id, SaveBankFormatProfile body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdateBankFormatProfile").Saves();
        finance.MapPut("/treasury-accounts/{id:int}/bank-format", (int id, BankFormatBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new SetTreasuryAccountBankFormat(id, body.BankFormatProfileId, body.ImportFolder), ct))
            .WithName("SetTreasuryAccountBankFormat").Produces<bool>().Problems();
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

/// <summary>Acceptarea golului de continuitate: motivul si versiunea extrasului.</summary>
public sealed record AcceptGapBody(string Reason, byte[] RowVersion);

/// <summary>Profilul bancar si folderul de import ale unui cont.</summary>
public sealed record BankFormatBody(int? BankFormatProfileId, string? ImportFolder);
