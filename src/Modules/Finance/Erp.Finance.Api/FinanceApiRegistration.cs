using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Fx;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Planning;
using Erp.Finance.Domain.Planning;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
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
        // Dupa import (tranzactia lui e inchisa), potrivirea automata ruleaza pe fiecare extras, in tranzactia ei (F2 4.1 pasul 5), daca
        // utilizatorul are dreptul de reconciliere.
        finance.MapPost("/bank-statements/import", async (ImportBankStatement body, ISender sender,
                [Microsoft.AspNetCore.Mvc.FromServices] Erp.Core.Contracts.ICurrentContext context, CancellationToken ct) =>
            {
                var result = await sender.Send(body, ct);
                if (context.HasPermission(FinancePermissions.BankStatementReconcile))
                {
                    foreach (var statement in result.Statements)
                    {
                        await sender.Send(new RunAutoMatching(statement.BankStatementId), ct);
                    }
                }

                return result;
            })
            .WithName("ImportBankStatement").Produces<BankImportResultDto>().Problems();
        finance.MapPost("/bank-statements/auto-match", (RunAutoMatching body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("RunAutoMatching").Produces<MatchingResultDto>().Problems();
        finance.MapGet("/matching-rules", (ISender sender, CancellationToken ct) => sender.Send(new GetMatchingRules(), ct))
            .WithName("GetMatchingRules").Produces<IReadOnlyList<MatchingRuleDto>>().Problems();
        finance.MapPost("/matching-rules", (SaveMatchingRule body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateMatchingRule").Saves();
        finance.MapPut("/matching-rules/{id:int}", (int id, SaveMatchingRule body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateMatchingRule").Saves();
        finance.MapPost("/matching-rules/reorder", (ReorderMatchingRules body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("ReorderMatchingRules").Produces<bool>().Problems();
        // Decontari cu partenerii (F3 6, 8): solduri, partide deschise, alocari, litigii, blocari, profil financiar.
        finance.MapGet("/partners/{partnerId:int}/balance", (int partnerId, PartnerRole? role, DateOnly? asOf, ISender sender, CancellationToken ct) =>
                sender.Send(new GetPartnerBalance(partnerId, role, asOf), ct))
            .WithName("GetPartnerBalance").Produces<IReadOnlyList<PartnerBalanceDto>>().Problems();
        finance.MapGet("/partners/{partnerId:int}/open-items", (int partnerId, PartnerRole? role, bool? onlyOpen, DateOnly? from, DateOnly? to, ISender sender,
                CancellationToken ct) => sender.Send(new GetOpenItems(partnerId, role, onlyOpen ?? true, from, to), ct))
            .WithName("GetOpenItems").Produces<IReadOnlyList<OpenItemListDto>>().Problems();
        finance.MapGet("/partners/{partnerId:int}/allocations", (int partnerId, PartnerRole? role, ISender sender, CancellationToken ct) =>
                sender.Send(new GetPartnerAllocations(partnerId, role), ct))
            .WithName("GetPartnerAllocations").Produces<IReadOnlyList<PartnerAllocationDto>>().Problems();
        finance.MapPost("/payment-orders/from-invoices", (CreatePaymentOrdersFromInvoices body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("CreatePaymentOrdersFromInvoices").Produces<IReadOnlyList<int>>().Problems();
        // Compensari (F3 3.4, 4.4; macheta f3-05).
        finance.MapGet("/compensations", (int? year, CompensationStatus? status, bool? withoutSigned, string? search, ISender sender, CancellationToken ct) =>
                sender.Send(new GetCompensations(year, status, withoutSigned ?? false, search), ct))
            .WithName("GetCompensations").Produces<IReadOnlyList<CompensationItemDto>>().Problems();
        finance.MapGet("/compensations/proposals", (ISender sender, CancellationToken ct) => sender.Send(new GetCompensationProposals(), ct))
            .WithName("GetCompensationProposals").Produces<IReadOnlyList<CompensationProposalDto>>().Problems();
        finance.MapGet("/compensations/propose", (int partnerId, string currencyCode, ISender sender, CancellationToken ct) =>
                sender.Send(new ProposeCompensation(partnerId, currencyCode), ct))
            .WithName("ProposeCompensation").Produces<IReadOnlyList<CompensationLineInput>>().Problems();
        finance.MapGet("/compensations/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetCompensation(id), ct))
            .WithName("GetCompensation").Produces<CompensationDetailDto>().Problems();
        finance.MapPost("/compensations", (SaveCompensation body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateCompensation").Saves();
        finance.MapPut("/compensations/{id:int}", (int id, SaveCompensation body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateCompensation").Saves();
        finance.MapPost("/compensations/{id:int}/delete", (int id, DeleteCompensation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("DeleteCompensation").Produces<bool>().Problems();
        finance.MapPost("/compensations/{id:int}/validate", (int id, ValidateCompensation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ValidateCompensation").Saves();
        finance.MapPost("/compensations/{id:int}/reverse", (int id, ReverseCompensation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ReverseCompensation").Saves();
        finance.MapPost("/compensations/{id:int}/signed", (int id, AttachCompensationScan body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("AttachCompensationScan").Saves();
        finance.MapGet("/compensations/{id:int}/signed", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetCompensationScan(id), ct))
            .WithName("GetCompensationScan").Produces<DownloadedFile>().Problems();
        finance.MapGet("/compensations/{id:int}/pdf", (int id, ISender sender, CancellationToken ct) => sender.Send(new PrintCompensation(id), ct))
            .WithName("PrintCompensation").Produces<DownloadedFile>().Problems();
        // Confirmari de sold (F3 3.9, 4.6; macheta f3-07).
        finance.MapGet("/balance-confirmations", (int? batchId, ISender sender, CancellationToken ct) => sender.Send(new GetBalanceConfirmations(batchId), ct))
            .WithName("GetBalanceConfirmations").Produces<BalanceConfirmationsDto>().Problems();
        finance.MapPost("/balance-confirmations", (GenerateBalanceConfirmations body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("GenerateBalanceConfirmations").Produces<int>().Problems();
        finance.MapPost("/balance-confirmations/batches/{batchId:int}/send", (int batchId, SendBalanceConfirmations body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { BatchId = batchId }, ct))
            .WithName("SendBalanceConfirmations").Produces<BalanceConfirmationsSent>().Problems();
        finance.MapPost("/balance-confirmations/{id:int}/response", (int id, RecordBalanceConfirmationResponse body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("RecordBalanceConfirmationResponse").Saves();
        finance.MapGet("/balance-confirmations/{id:int}/pdf", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetBalanceConfirmationPdf(id), ct))
            .WithName("GetBalanceConfirmationPdf").Produces<DownloadedFile>().Problems();
        finance.MapGet("/balance-confirmations/{id:int}/response-file", (int id, ISender sender, CancellationToken ct) =>
                sender.Send(new GetBalanceConfirmationResponseFile(id), ct))
            .WithName("GetBalanceConfirmationResponseFile").Produces<DownloadedFile>().Problems();
        // Penalitati de intarziere si ratele de referinta (F3 3.10, 4.7; macheta f3-08).
        finance.MapGet("/penalties", (int? year, PenaltyStatus? status, ISender sender, CancellationToken ct) => sender.Send(new GetPenaltyCalculations(year, status), ct))
            .WithName("GetPenaltyCalculations").Produces<IReadOnlyList<PenaltyCalculationItemDto>>().Problems();
        finance.MapGet("/penalties/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPenaltyCalculation(id), ct))
            .WithName("GetPenaltyCalculation").Produces<PenaltyCalculationDto>().Problems();
        finance.MapPost("/penalties", (CalculatePenalties body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("CalculatePenalties").Saves();
        finance.MapPost("/penalties/{id:int}/recalculate", (int id, RecalculatePenalties body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("RecalculatePenalties").Saves();
        finance.MapPost("/penalties/{id:int}/remove-lines", (int id, RemovePenaltyLines body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("RemovePenaltyLines").Saves();
        finance.MapPost("/penalties/{id:int}/approve", (int id, ApprovePenaltyCalculation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ApprovePenaltyCalculation").Saves();
        finance.MapPost("/penalties/{id:int}/cancel", (int id, CancelPenaltyCalculation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("CancelPenaltyCalculation").Saves();
        finance.MapGet("/reference-rates", (ISender sender, CancellationToken ct) => sender.Send(new GetReferenceRates(), ct))
            .WithName("GetReferenceRates").Produces<IReadOnlyList<ReferenceRateDto>>().Problems();
        finance.MapPost("/reference-rates", (SaveReferenceRate body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveReferenceRate").Produces<bool>().Problems();
        // Soldurile initiale (F3 4.9; mecanismul de import 02.01, 4.8): sablon, incarcare si validare, confirmare in loturi, randuri cu erori.
        finance.MapGet("/opening-balances/template", (ISender sender, CancellationToken ct) => sender.Send(new GetOpeningBalanceTemplate(), ct))
            .WithName("GetOpeningBalanceTemplate").Produces<DownloadedFile>().Problems();
        finance.MapPost("/opening-balances", (UploadOpeningBalances body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("UploadOpeningBalances").Produces<OpeningImportDto>().Problems();
        finance.MapGet("/opening-balances/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetOpeningBalanceImport(id), ct))
            .WithName("GetOpeningBalanceImport").Produces<OpeningImportDto>().Problems();
        finance.MapGet("/opening-balances/{id:int}/errors", (int id, ISender sender, CancellationToken ct) => sender.Send(new DownloadOpeningBalanceErrors(id), ct))
            .WithName("DownloadOpeningBalanceErrors").Produces<DownloadedFile>().Problems();
        finance.MapPost("/opening-balances/{id:int}/cancel", (int id, ISender sender, CancellationToken ct) => sender.Send(new CancelOpeningBalanceImport(id), ct))
            .WithName("CancelOpeningBalanceImport").Produces<OpeningImportDto>().Problems();
        // Confirmarea: loturi de cate 500 de randuri, fiecare in tranzactia lui (02.01, 4.8 pasul 4); un lot esuat lasa importul partial.
        finance.MapPost("/opening-balances/{id:int}/confirm", async (int id, ISender sender, CancellationToken ct) =>
            {
                try
                {
                    while (!(await sender.Send(new ImportOpeningOpenItems(id), ct)).Done)
                    {
                    }
                }
                catch (ErpException ex) when (ex.Code is not ("CORE_IMPORT_NOT_PENDING" or "CORE_ENTITY_NOT_FOUND" or CoreErrorCodes.Forbidden))
                {
                    return await sender.Send(new FailOpeningBalanceImport(id, ex.Message), CancellationToken.None);
                }

                return await sender.Send(new GetOpeningBalanceImport(id), ct);
            })
            .WithName("ConfirmOpeningBalanceImport").Produces<OpeningImportDto>().Problems();
        // Propunerile de plata (F5 3.1–3.3, 4.1–4.3; machetele f5-01, f5-02, cf-09).
        finance.MapGet("/payment-proposals", (ProposalStatus? status, bool? pendingForMe, ISender sender, CancellationToken ct) =>
                sender.Send(new GetPaymentProposals(status, pendingForMe ?? false), ct))
            .WithName("GetPaymentProposals").Produces<IReadOnlyList<ProposalItemDto>>().Problems();
        finance.MapGet("/payment-proposals/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPaymentProposal(id), ct))
            .WithName("GetPaymentProposal").Produces<ProposalDto>().Problems();
        finance.MapPost("/payment-proposals", (BuildPaymentProposal body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("BuildPaymentProposal").Saves();
        finance.MapPut("/payment-proposals/{id:int}", (int id, SavePaymentProposal body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("SavePaymentProposal").Saves();
        finance.MapPost("/payment-proposals/{id:int}/delete", (int id, DeletePaymentProposal body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("DeletePaymentProposal").Produces<bool>().Problems();
        finance.MapPost("/payment-proposals/{id:int}/submit", (int id, SubmitPaymentProposal body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("SubmitPaymentProposal").Saves();
        finance.MapPost("/payment-proposals/{id:int}/withdraw", (int id, WithdrawPaymentProposal body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("WithdrawPaymentProposal").Saves();
        finance.MapPost("/payment-proposals/{id:int}/decide", (int id, DecidePaymentProposal body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("DecidePaymentProposal").Saves();
        finance.MapPost("/payment-proposals/{id:int}/generate", (int id, ISender sender, CancellationToken ct) => sender.Send(new GeneratePaymentOrders(id), ct))
            .WithName("GeneratePaymentOrders").Produces<GeneratedOrdersDto>().Problems();
        finance.MapGet("/payment-approval-rules", (ISender sender, CancellationToken ct) => sender.Send(new GetPaymentApprovalRules(), ct))
            .WithName("GetPaymentApprovalRules").Produces<ApprovalRulesDto>().Problems();
        finance.MapPut("/payment-approval-rules", (SavePaymentApprovalRules body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SavePaymentApprovalRules").Produces<ApprovalRulesDto>().Problems();
        // Prognoza de cash-flow (F5 3.4, 3.5, 4.4; machetele f5-03, cf-10).
        finance.MapGet("/cash-flow/forecast", (DateOnly? from, DateOnly? to, Granularity? granularity, int? treasuryAccountId, string? currencyCode, bool? prudent,
                int? extraDays, ISender sender, CancellationToken ct) =>
                sender.Send(new GetCashFlowForecast(from, to, granularity ?? Granularity.Week, treasuryAccountId, currencyCode, prudent ?? false, extraDays ?? 0), ct))
            .WithName("GetCashFlowForecast").Produces<CashFlowForecastDto>().Problems();
        finance.MapGet("/cash-flow/categories", (ISender sender, CancellationToken ct) => sender.Send(new GetCashFlowCategories(), ct))
            .WithName("GetCashFlowCategories").Produces<IReadOnlyList<CashFlowCategoryDto>>().Problems();
        finance.MapGet("/cash-flow/plan-items", (bool? includeInactive, ISender sender, CancellationToken ct) =>
                sender.Send(new GetCashFlowPlanItems(includeInactive ?? false), ct))
            .WithName("GetCashFlowPlanItems").Produces<IReadOnlyList<CashFlowPlanItemDto>>().Problems();
        finance.MapPost("/cash-flow/plan-items", (SaveCashFlowPlanItem body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateCashFlowPlanItem").Produces<int>().Problems();
        finance.MapPut("/cash-flow/plan-items/{id:int}", (int id, SaveCashFlowPlanItem body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateCashFlowPlanItem").Produces<int>().Problems();
        finance.MapDelete("/cash-flow/plan-items/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new DeleteCashFlowPlanItem(id), ct))
            .WithName("DeleteCashFlowPlanItem").Produces<bool>().Problems();
        finance.MapGet("/cash-flow/delay-buckets", (ISender sender, CancellationToken ct) => sender.Send(new GetPaymentDelayBuckets(), ct))
            .WithName("GetPaymentDelayBuckets").Produces<IReadOnlyList<DelayBucketDto>>().Problems();
        finance.MapPut("/cash-flow/delay-buckets", (SavePaymentDelayBuckets body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SavePaymentDelayBuckets").Produces<IReadOnlyList<DelayBucketDto>>().Problems();
        finance.MapPost("/cash-flow/payment-behavior/recalculate", (ISender sender, CancellationToken ct) => sender.Send(new RecalculatePaymentBehavior(), ct))
            .WithName("RecalculatePaymentBehavior").Produces<int>().Problems();
        finance.MapGet("/cash-flow/snapshots", (ISender sender, CancellationToken ct) => sender.Send(new GetCashFlowSnapshots(), ct))
            .WithName("GetCashFlowSnapshots").Produces<IReadOnlyList<CashFlowSnapshotItemDto>>().Problems();
        finance.MapPost("/cash-flow/snapshots", (TakeCashFlowSnapshot body, ISender sender, CancellationToken ct) => sender.Send(body with { IsAutomatic = false }, ct))
            .WithName("TakeCashFlowSnapshot").Produces<int>().Problems();
        finance.MapGet("/cash-flow/snapshots/{id:int}/vs-actual", (int id, ISender sender, CancellationToken ct) =>
                sender.Send(new GetCashFlowForecastVsActual(id), ct))
            .WithName("GetCashFlowForecastVsActual").Produces<CashFlowVsActualDto>().Problems();
        finance.MapGet("/cash-flow/alert-rules", (ISender sender, CancellationToken ct) => sender.Send(new GetCashFlowAlertRules(), ct))
            .WithName("GetCashFlowAlertRules").Produces<IReadOnlyList<CashFlowAlertRuleDto>>().Problems();
        finance.MapPut("/cash-flow/alert-rules", (SaveCashFlowAlertRules body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveCashFlowAlertRules").Produces<IReadOnlyList<CashFlowAlertRuleDto>>().Problems();
        // Planul de conturi (C1 3.1–3.3; macheta c1-01).
        finance.MapGet("/charts", (ISender sender, CancellationToken ct) => sender.Send(new GetCharts(), ct))
            .WithName("GetCharts").Produces<IReadOnlyList<ChartDto>>().Problems();
        finance.MapGet("/gl-accounts", (int? chartId, bool? includeInactive, ISender sender, CancellationToken ct) =>
                sender.Send(new GetGlAccounts(chartId, includeInactive ?? false), ct))
            .WithName("GetGlAccounts").Produces<IReadOnlyList<GlAccountItemDto>>().Problems();
        finance.MapGet("/gl-accounts/import-template", (ISender sender, CancellationToken ct) => sender.Send(new GetGlAccountImportTemplate(), ct))
            .WithName("GetGlAccountImportTemplate").Produces<DownloadedFile>().Problems();
        finance.MapPost("/gl-accounts/import", (ImportGlAccounts body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("ImportGlAccounts").Produces<GlAccountImportDto>().Problems();
        finance.MapGet("/gl-accounts/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetGlAccount(id), ct))
            .WithName("GetGlAccount").Produces<GlAccountDetailDto>().Problems();
        finance.MapPost("/gl-accounts", (SaveGlAccount body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateGlAccount").Produces<FinanceSaved>().Problems();
        finance.MapPut("/gl-accounts/{id:int}", (int id, SaveGlAccount body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateGlAccount").Produces<FinanceSaved>().Problems();
        finance.MapPut("/gl-accounts/{id:int}/active", (int id, SetGlAccountActive body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("SetGlAccountActive").Produces<FinanceSaved>().Problems();
        finance.MapPut("/gl-accounts/{id:int}/dimension-rules", (int id, SaveGlAccountDimensionRules body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { AccountId = id }, ct))
            .WithName("SaveGlAccountDimensionRules").Produces<bool>().Problems();
        // Planurile alternative si regulile de mapare (C1 3.5, 4.2, 4.5; machetele c1-02…c1-04).
        finance.MapGet("/charts/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetAltChart(id), ct))
            .WithName("GetAltChart").Produces<AltChartDto>().Problems();
        finance.MapPost("/charts", (SaveAltChart body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateAltChart").Produces<FinanceSaved>().Problems();
        finance.MapPut("/charts/{id:int}", (int id, SaveAltChart body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateAltChart").Produces<FinanceSaved>().Problems();
        finance.MapPost("/charts/{id:int}/accounts", (int id, SaveAltGlAccount body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, ChartId = id }, ct))
            .WithName("CreateAltGlAccount").Produces<FinanceSaved>().Problems();
        finance.MapPut("/charts/{id:int}/accounts/{accountId:int}", (int id, int accountId, SaveAltGlAccount body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = accountId, ChartId = id }, ct))
            .WithName("UpdateAltGlAccount").Produces<FinanceSaved>().Problems();
        finance.MapPost("/charts/{id:int}/accounts/import", (int id, ImportAltGlAccounts body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { ChartId = id }, ct))
            .WithName("ImportAltGlAccounts").Produces<GlAccountImportDto>().Problems();
        finance.MapGet("/charts/{id:int}/rules", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetMappingRules(id), ct))
            .WithName("GetMappingRules").Produces<IReadOnlyList<MappingRuleDto>>().Problems();
        finance.MapPost("/charts/{id:int}/rules", (int id, SaveMappingRule body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, ChartId = id }, ct))
            .WithName("CreateMappingRule").Produces<FinanceSaved>().Problems();
        finance.MapPut("/charts/{id:int}/rules/{ruleId:int}", (int id, int ruleId, SaveMappingRule body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = ruleId, ChartId = id }, ct))
            .WithName("UpdateMappingRule").Produces<FinanceSaved>().Problems();
        finance.MapPut("/charts/{id:int}/rules/order", (int id, ReorderMappingRules body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { ChartId = id }, ct))
            .WithName("ReorderMappingRules").Produces<bool>().Problems();
        finance.MapGet("/charts/{id:int}/simulate", (int id, string accountCode, byte? side, int? partnerId, int? costCenterId, int? projectId,
                int? warehouseId, int? siteId, int? itemGroupId, string? currencyCode, DateOnly? onDate, ISender sender, CancellationToken ct) =>
                sender.Send(new SimulateMapping(id, accountCode, side ?? 0, partnerId, costCenterId, projectId, warehouseId, siteId, itemGroupId, currencyCode,
                    onDate), ct))
            .WithName("SimulateMapping").Produces<SimulationDto>().Problems();
        finance.MapGet("/charts/{id:int}/check", (int id, ISender sender, CancellationToken ct) => sender.Send(new CheckMapping(id), ct))
            .WithName("CheckMapping").Produces<MappingCheckDto>().Problems();
        finance.MapGet("/gl-account-roles", (ISender sender, CancellationToken ct) => sender.Send(new GetGlAccountRoles(), ct))
            .WithName("GetGlAccountRoles").Produces<IReadOnlyList<RoleAssignmentDto>>().Problems();
        finance.MapPut("/gl-account-roles", (SaveGlAccountRoles body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveGlAccountRoles").Produces<IReadOnlyList<RoleAssignmentDto>>().Problems();
        // Perioadele contabile (C3 3.1, 3.2; macheta c3-01).
        finance.MapGet("/periods", (ISender sender, CancellationToken ct) => sender.Send(new GetAccountingPeriods(), ct))
            .WithName("GetAccountingPeriods").Produces<IReadOnlyList<FiscalYearDto>>().Problems();
        finance.MapGet("/periods/{id:int}/history", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPeriodHistory(id), ct))
            .WithName("GetPeriodHistory").Produces<IReadOnlyList<PeriodHistoryDto>>().Problems();
        finance.MapPost("/fiscal-years", (CreateFiscalYear body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("CreateFiscalYear").Produces<int>().Problems();
        finance.MapPost("/periods/{id:int}/soft-close", (int id, SoftClosePeriod body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("SoftClosePeriod").Produces<FinanceSaved>().Problems();
        finance.MapPost("/periods/{id:int}/close", (int id, ClosePeriod body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("ClosePeriod").Produces<FinanceSaved>().Problems();
        finance.MapPost("/periods/{id:int}/reopen", (int id, ReopenPeriod body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("ReopenPeriod").Produces<FinanceSaved>().Problems();
        // Reevaluarea valutara (F4 3.6, 4.4, 4.5; macheta f4-01).
        finance.MapGet("/fx/revaluations", (int? year, ISender sender, CancellationToken ct) => sender.Send(new GetFxRevaluations(year), ct))
            .WithName("GetFxRevaluations").Produces<IReadOnlyList<FxRunItemDto>>().Problems();
        finance.MapGet("/fx/revaluations/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetFxRevaluation(id), ct))
            .WithName("GetFxRevaluation").Produces<FxRunDto>().Problems();
        finance.MapGet("/fx/differences", (DateOnly? from, DateOnly? to, byte? source, string? currencyCode, int? partnerId, string? search, ISender sender,
                CancellationToken ct) => sender.Send(new GetFxDifferences(from, to, source, currencyCode, partnerId, search), ct))
            .WithName("GetFxDifferences").Produces<FxDifferencesDto>().Problems();
        finance.MapGet("/fx/exposure", (DateOnly? asOf, bool? includeAdvances, ISender sender, CancellationToken ct) =>
                sender.Send(new GetFxExposure(asOf, includeAdvances), ct))
            .WithName("GetFxExposure").Produces<FxExposureDto>().Problems();
        finance.MapGet("/fx/settings", (ISender sender, CancellationToken ct) => sender.Send(new GetFxSettings(), ct))
            .WithName("GetFxSettings").Produces<FxSettingsDto>().Problems();
        finance.MapPut("/fx/settings", (SaveFxSettings body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveFxSettings").Produces<FxSettingsDto>().Problems();
        finance.MapGet("/fx/treasury-values", (DateOnly? asOf, int? treasuryAccountId, ISender sender, CancellationToken ct) =>
                sender.Send(new GetTreasuryFxValues(asOf, treasuryAccountId), ct))
            .WithName("GetTreasuryFxValues").Produces<IReadOnlyList<TreasuryRonValueDto>>().Problems();
        finance.MapPost("/fx/revaluations", (GenerateFxRevaluation body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("GenerateFxRevaluation").Produces<int>().Problems();
        finance.MapPost("/fx/revaluations/{id:int}/validate", (int id, ValidateFxRevaluation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ValidateFxRevaluation").Saves();
        finance.MapPost("/fx/revaluations/{id:int}/reverse", (int id, ReverseFxRevaluation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ReverseFxRevaluation").Saves();
        finance.MapPost("/fx/revaluations/{id:int}/delete", (int id, DeleteFxRevaluationDraft body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("DeleteFxRevaluationDraft").Produces<bool>().Problems();
        // Efecte comerciale (F3 3.5, 4.5; macheta f3-06).
        finance.MapGet("/commercial-papers", (PaperDirection? direction, bool? activeOnly, DateOnly? maturityTo, int? year, ISender sender, CancellationToken ct) =>
                sender.Send(new GetCommercialPapers(direction, activeOnly ?? false, maturityTo, year), ct))
            .WithName("GetCommercialPapers").Produces<CommercialPaperListDto>().Problems();
        finance.MapGet("/commercial-papers/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetCommercialPaper(id), ct))
            .WithName("GetCommercialPaper").Produces<CommercialPaperDetailDto>().Problems();
        finance.MapPost("/commercial-papers", (RegisterCommercialPaper body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("RegisterCommercialPaper").Saves();
        finance.MapPost("/commercial-papers/{id:int}/status", (int id, ChangeCommercialPaperStatus body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ChangeCommercialPaperStatus").Saves();
        finance.MapGet("/due-schedule", (DateOnly? from, DateOnly? to, ISender sender, CancellationToken ct) => sender.Send(new GetDueSchedule(from, to), ct))
            .WithName("GetDueSchedule").Produces<DueScheduleDto>().Problems();
        finance.MapGet("/due-schedule/items", (PartnerRole role, DateOnly day, bool? overdue, ISender sender, CancellationToken ct) =>
                sender.Send(new GetDueItems(role, day, overdue ?? false), ct))
            .WithName("GetDueItems").Produces<IReadOnlyList<DueItemDto>>().Problems();
        finance.MapGet("/partner-balances", (DateOnly? asOf, PartnerRole? role, string? search, bool? onlyOverdue, bool? onlyUnallocated, bool? bothRoles,
                ISender sender, CancellationToken ct) =>
                sender.Send(new GetPartnerBalances(asOf, role, search, onlyOverdue ?? false, onlyUnallocated ?? false, bothRoles ?? false), ct))
            .WithName("GetPartnerBalances").Produces<PartnerBalanceListDto>().Problems();
        finance.MapGet("/partners/{partnerId:int}/finance-profile/{role}", (int partnerId, PartnerRole role, ISender sender, CancellationToken ct) =>
                sender.Send(new GetPartnerFinanceProfile(partnerId, role), ct))
            .WithName("GetPartnerFinanceProfile").Produces<PartnerFinanceProfileDto>().Problems();
        finance.MapPut("/partners/{partnerId:int}/finance-profile/{role}", (int partnerId, PartnerRole role, SavePartnerFinanceProfile body, ISender sender,
                CancellationToken ct) => sender.Send(body with { PartnerId = partnerId, Role = role }, ct))
            .WithName("SavePartnerFinanceProfile").Produces<byte[]>().Problems();
        finance.MapPost("/allocations", (AllocateManually body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("AllocateManually").Produces<AllocationsCreatedDto>().Problems();
        finance.MapGet("/write-offs/threshold", (ISender sender, CancellationToken ct) => sender.Send(new GetWriteOffThreshold(), ct))
            .WithName("GetWriteOffThreshold").Produces<string>().Problems();
        finance.MapPost("/write-offs", (CreateWriteOffs body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("CreateWriteOffs").Produces<IReadOnlyList<int>>().Problems();
        finance.MapPost("/allocations/{id:long}/undo", (long id, UndoAllocation body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { AllocationId = id }, ct))
            .WithName("UndoAllocation").Produces<bool>().Problems();
        finance.MapPost("/open-items/{id:long}/dispute", (long id, SetOpenItemDispute body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { OpenItemId = id }, ct))
            .WithName("SetOpenItemDispute").Produces<byte[]>().Problems();
        finance.MapPost("/open-items/{id:long}/payment-block", (long id, SetOpenItemPaymentBlock body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { OpenItemId = id }, ct))
            .WithName("SetOpenItemPaymentBlock").Produces<byte[]>().Problems();
        finance.MapPost("/matching-rules/test", (TestMatchingRule body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("TestMatchingRule").Produces<MatchingRuleTestDto>().Problems();
        finance.MapPost("/bank-statement-lines/{id:long}/remember", (long id, RememberMatchingRule body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { BankStatementLineId = id }, ct))
            .WithName("RememberMatchingRule").Saves();
        finance.MapGet("/card-acquirers", (ISender sender, CancellationToken ct) => sender.Send(new GetCardAcquirers(), ct))
            .WithName("GetCardAcquirers").Produces<IReadOnlyList<CardAcquirerDto>>().Problems();
        finance.MapPost("/card-acquirers", (SaveCardAcquirer body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateCardAcquirer").Saves();
        finance.MapPut("/card-acquirers/{id:int}", (int id, SaveCardAcquirer body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateCardAcquirer").Saves();
        finance.MapGet("/card-settlements", ([AsParameters] GetCardSettlements query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetCardSettlements").Produces<IReadOnlyList<CardSettlementDto>>().Problems();
        finance.MapPost("/card-settlements", (RegisterCardSettlement body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("RegisterCardSettlement").Produces<int>().Problems();
        finance.MapPost("/card-settlements/{id:int}/resolve", (int id, ResolveCardBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ResolveCardSettlement(id, body.Note, body.RowVersion), ct))
            .WithName("ResolveCardSettlement").Produces<byte[]>().Problems();
        finance.MapGet("/transit-items", ([AsParameters] GetTransitItems query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetTransitItems").Produces<IReadOnlyList<TransitItemDto>>().Problems();
        finance.MapPost("/bank-statements/{id:int}/accept-gap", (int id, AcceptGapBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new AcceptStatementGap(id, body.Reason, body.RowVersion), ct))
            .WithName("AcceptStatementGap").Produces<byte[]>().Problems();
        finance.MapGet("/bank-lookups", (ISender sender, CancellationToken ct) => sender.Send(new GetBankLookups(), ct))
            .WithName("GetBankLookups").Produces<BankLookupsDto>().Problems();
        finance.MapGet("/bank-accounts", (ISender sender, CancellationToken ct) => sender.Send(new GetBankAccounts(), ct))
            .WithName("GetBankAccounts").Produces<IReadOnlyList<BankAccountLookupDto>>().Problems();
        finance.MapGet("/bank-reconciliation/{accountId:int}", (int accountId, ISender sender, CancellationToken ct) =>
                sender.Send(new GetBankReconciliation(accountId), ct))
            .WithName("GetBankReconciliation").Produces<BankReconciliationDto>().Problems();
        finance.MapGet("/bank-statement-lines/{id:long}", (long id, ISender sender, CancellationToken ct) => sender.Send(new GetBankStatementLine(id), ct))
            .WithName("GetBankStatementLine").Produces<BankLineDetailDto>().Problems();
        finance.MapPut("/bank-statement-lines/{id:long}/explanation", (long id, ExplainLineBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ExplainBankStatementLine(id, body.Parts, body.RowVersion), ct))
            .WithName("ExplainBankStatementLine").Produces<BankLineSaved>().Problems();
        finance.MapPost("/bank-statement-lines/validate", (ValidateBankStatementLines body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("ValidateBankStatementLines").Produces<IReadOnlyList<BankLineValidationResult>>().Problems();
        finance.MapPost("/bank-statement-lines/{id:long}/unvalidate", (long id, AcceptGapBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new UnvalidateBankStatementLine(id, body.Reason, body.RowVersion), ct))
            .WithName("UnvalidateBankStatementLine").Produces<BankLineSaved>().Problems();
        finance.MapGet("/payment-orders", ([AsParameters] GetPaymentOrders query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetPaymentOrders").Produces<IReadOnlyList<PaymentOrderItemDto>>().Problems();
        finance.MapGet("/payment-orders/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPaymentOrder(id), ct))
            .WithName("GetPaymentOrder").Produces<PaymentOrderDetailDto>().Problems();
        finance.MapPost("/payment-orders", (SavePaymentOrder body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreatePaymentOrder").Saves();
        finance.MapPut("/payment-orders/{id:int}", (int id, SavePaymentOrder body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdatePaymentOrder").Saves();
        finance.MapPost("/payment-orders/{id:int}/delete", (int id, RowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new DeletePaymentOrder(id, body.RowVersion), ct))
            .WithName("DeletePaymentOrder").Produces<bool>().Problems();
        finance.MapPost("/payment-orders/{id:int}/validate", (int id, ValidateOrderBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ValidatePaymentOrder(id, body.RowVersion, body.ConfirmAnafInactive), ct))
            .WithName("ValidatePaymentOrder").Saves();
        finance.MapPost("/payment-orders/{id:int}/reverse", (int id, RowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ReversePaymentOrder(id, body.RowVersion), ct))
            .WithName("ReversePaymentOrder").Saves();
        finance.MapPost("/payment-orders/approve", (ApprovePaymentOrders body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("ApprovePaymentOrders").Produces<IReadOnlyList<PaymentOrderVersion>>().Problems();
        finance.MapPost("/payment-orders/unapprove", (UnapprovePaymentOrders body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("UnapprovePaymentOrders").Produces<IReadOnlyList<PaymentOrderVersion>>().Problems();
        finance.MapGet("/payment-batches", ([AsParameters] GetPaymentBatches query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetPaymentBatches").Produces<IReadOnlyList<PaymentBatchItemDto>>().Problems();
        finance.MapGet("/payment-batches/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPaymentBatch(id), ct))
            .WithName("GetPaymentBatch").Produces<PaymentBatchDetailDto>().Problems();
        finance.MapGet("/payment-batches/{id:int}/file", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetPaymentBatchFile(id), ct))
            .WithName("GetPaymentBatchFile").Produces<PaymentBatchFileDto>().Problems();
        finance.MapPost("/payment-batches", (SavePaymentBatch body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreatePaymentBatch").Saves();
        finance.MapPut("/payment-batches/{id:int}", (int id, SavePaymentBatch body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdatePaymentBatch").Saves();
        finance.MapPost("/payment-batches/{id:int}/export", (int id, ExportBatchBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ExportPaymentBatch(id, body.RowVersion, body.ReExportReason), ct))
            .WithName("ExportPaymentBatch").Produces<PaymentBatchFileDto>().Problems();
        finance.MapPost("/payment-batches/{id:int}/cancel", (int id, AcceptGapBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new CancelPaymentBatch(id, body.Reason, body.RowVersion), ct))
            .WithName("CancelPaymentBatch").Produces<byte[]>().Problems();
        finance.MapPost("/bank-statement-lines/{id:long}/payment-order", (long id, CreatePaymentOrderFromStatementLine body, ISender sender,
                CancellationToken ct) => sender.Send(body with { BankStatementLineId = id }, ct))
            .WithName("CreatePaymentOrderFromStatementLine").Saves();
        finance.MapGet("/settings", (ISender sender, CancellationToken ct) => sender.Send(new GetFinanceSettings(), ct))
            .WithName("GetFinanceSettings").Produces<FinanceSettingsDto>().Problems();
        finance.MapPut("/settings", (SaveFinanceSettings body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveFinanceSettings").Produces<FinanceSettingsDto>().Problems();
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

/// <summary>O actiune cu motiv (acceptarea golului, desfacerea validarii): motivul si versiunea vazuta.</summary>
public sealed record AcceptGapBody(string Reason, byte[] RowVersion);

/// <summary>Explicatia unei decontari de card cu diferenta.</summary>
public sealed record ResolveCardBody(string Note, byte[] RowVersion);

/// <summary>Exportul unui lot: versiunea si, la reexport, motivul.</summary>
public sealed record ExportBatchBody(byte[] RowVersion, string? ReExportReason = null);

/// <summary>Validarea unui OP: versiunea si confirmarea platii catre un partener inactiv la ANAF.</summary>
public sealed record ValidateOrderBody(byte[] RowVersion, bool ConfirmAnafInactive = false);

/// <summary>Explicatia unei linii de extras: partile si versiunea liniei.</summary>
public sealed record ExplainLineBody(IReadOnlyList<BankLinePartInput> Parts, byte[] RowVersion);

/// <summary>Profilul bancar si folderul de import ale unui cont.</summary>
public sealed record BankFormatBody(int? BankFormatProfileId, string? ImportFolder);
