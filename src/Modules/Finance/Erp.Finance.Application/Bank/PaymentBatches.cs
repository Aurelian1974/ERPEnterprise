using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;
using FluentValidation;

namespace Erp.Finance.Application.Bank;

public enum PaymentBatchStatus : byte
{
    Open = 1,
    Exported = 2,
    Cancelled = 3,
}

/// <summary>Un OP din lot, cu ce scrie fisierul de plati.</summary>
public sealed record PaymentBatchOrder(int Id, string? Number, PaymentOrderStatus Status, int? ReversalOfId, PaymentConfirmationStatus? ConfirmationStatus,
    DateTime? ApprovedAt, PaymentOrderOrigin Origin, int? PartnerId, string BeneficiaryName, string BeneficiaryIban, string? BeneficiaryBic, decimal Amount,
    string? RemittanceInfo, string? EndToEndId, DateOnly RequestedExecutionDate, bool IsUrgent, byte[] RowVersion);

/// <summary>Lotul cu platitorul (firma, IBAN-ul contului, BIC-ul bancii) si profilul bancar, pentru export si afisare.</summary>
public sealed record PaymentBatchData(int Id, int TreasuryAccountId, string AccountCode, string CurrencyCode, string? AccountIban, string? BankBic,
    PaymentFileFormat? PaymentFormat, string? PaymentMapping, short MaxRemittanceLength, string CompanyName, string CompanyFiscalCode, string? Number,
    PaymentFileFormat? Format, int OrderCount, decimal TotalAmount, PaymentBatchStatus Status, int ExportCount, DateTime? ExportedAt, string? ExportedByName,
    string? FileName, string? FileSha256, string? LastReExportReason, DateTime? CancelledAt, string? CancelReason, IReadOnlyList<PaymentBatchOrder> Orders,
    byte[] RowVersion);

/// <summary>Fisierul de plati generat: numele, tipul si continutul.</summary>
public sealed record PaymentFile(string FileName, string ContentType, byte[] Content);

/// <summary>Scriitorul unui format de plati (F2 6: Pain001Writer, CsvPaymentWriter), in Infrastructure/Bank/Formats.</summary>
public interface IPaymentFileWriter
{
    bool Supports(PaymentFileFormat format);

    /// <summary>Fisierul lotului; createdAt e momentul exportului (GrpHdr/CreDtTm), ora Romaniei.</summary>
    PaymentFile Write(PaymentBatchData batch, PaymentFileFormat format, DateTime createdAt);
}

public sealed record PaymentBatchItemDto(int Id, int TreasuryAccountId, string AccountCode, string? Number, PaymentFileFormat? Format, int OrderCount,
    string TotalAmount, PaymentBatchStatus Status, int ExportCount, DateTime? ExportedAt, byte[] RowVersion);

public sealed record PaymentBatchOrderDto(int Id, string? Number, PaymentOrderStatus Status, PaymentConfirmationStatus? ConfirmationStatus, bool IsApproved,
    PaymentOrderOrigin Origin, string BeneficiaryName, string BeneficiaryIban, string Amount, string? RemittanceInfo, string? EndToEndId,
    DateOnly RequestedExecutionDate, bool IsUrgent);

public sealed record PaymentBatchDetailDto(int Id, int TreasuryAccountId, string AccountCode, string CurrencyCode, string? Number, PaymentFileFormat? Format,
    PaymentFileFormat? ProfileFormat, int OrderCount, string TotalAmount, PaymentBatchStatus Status, int ExportCount, DateTime? ExportedAt,
    string? ExportedByName, string? FileName, string? FileSha256, string? LastReExportReason, DateTime? CancelledAt, string? CancelReason,
    IReadOnlyList<PaymentBatchOrderDto> Orders, byte[] RowVersion);

public sealed record PaymentBatchFileDto(string FileName, string ContentType, string Sha256, string ContentBase64);

public interface IPaymentBatchStore
{
    Task<FinanceSaved> SaveAsync(int companyId, int? id, int treasuryAccountId, IReadOnlyList<int> orderIds, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<PaymentBatchData?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<PaymentBatchItemDto>> ListAsync(int companyId, int? treasuryAccountId, CancellationToken ct);

    Task<byte[]> MarkExportedAsync(int companyId, int id, byte[] rowVersion, PaymentFileFormat format, PaymentFile file, string sha256, string? reExportReason,
        bool requireApproval, int userId, CancellationToken ct);

    Task<byte[]> CancelAsync(int companyId, int id, string reason, byte[] rowVersion, int userId, CancellationToken ct);

    Task<(string FileName, string Sha256, byte[] Content)?> GetFileAsync(int companyId, int id, CancellationToken ct);
}

internal static class PaymentBatchMapping
{
    public static ErpException NotFound() => ErpException.NotFound("FIN_PAYMENT_BATCH_NOT_FOUND", "Lotul de plăți nu există.");

    public static string ContentType(string fileName) => fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? "application/xml" : "text/csv";

    public static PaymentBatchDetailDto Detail(PaymentBatchData b) => new(b.Id, b.TreasuryAccountId, b.AccountCode, b.CurrencyCode, b.Number, b.Format,
        b.PaymentFormat, b.OrderCount, Money.Format(b.TotalAmount), b.Status, b.ExportCount, b.ExportedAt, b.ExportedByName, b.FileName, b.FileSha256,
        b.LastReExportReason, b.CancelledAt, b.CancelReason,
        b.Orders.Select(o => new PaymentBatchOrderDto(o.Id, o.Number, o.Status, o.ConfirmationStatus, o.ApprovedAt is not null, o.Origin, o.BeneficiaryName,
            o.BeneficiaryIban, Money.Format(o.Amount), o.RemittanceInfo, o.EndToEndId, o.RequestedExecutionDate, o.IsUrgent)).ToList(), b.RowVersion);
}

/// <summary>Loturile de plati ale firmei (macheta f2-03).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetPaymentBatches(int? TreasuryAccountId = null) : IQuery<IReadOnlyList<PaymentBatchItemDto>>;

internal sealed class GetPaymentBatchesHandler(IPaymentBatchStore store, ICurrentContext context) : IQueryHandler<GetPaymentBatches, IReadOnlyList<PaymentBatchItemDto>>
{
    public Task<IReadOnlyList<PaymentBatchItemDto>> Handle(GetPaymentBatches request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.TreasuryAccountId, cancellationToken);
}

/// <summary>Un lot de plati cu OP-urile lui.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetPaymentBatch(int Id) : IQuery<PaymentBatchDetailDto>;

internal sealed class GetPaymentBatchHandler(IPaymentBatchStore store, ICurrentContext context) : IQueryHandler<GetPaymentBatch, PaymentBatchDetailDto>
{
    public async Task<PaymentBatchDetailDto> Handle(GetPaymentBatch request, CancellationToken cancellationToken) =>
        PaymentBatchMapping.Detail(await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PaymentBatchMapping.NotFound());
}

/// <summary>Fisierul ultimului export al unui lot, pentru descarcare.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderExport)]
public sealed record GetPaymentBatchFile(int Id) : IQuery<PaymentBatchFileDto>;

internal sealed class GetPaymentBatchFileHandler(IPaymentBatchStore store, ICurrentContext context) : IQueryHandler<GetPaymentBatchFile, PaymentBatchFileDto>
{
    public async Task<PaymentBatchFileDto> Handle(GetPaymentBatchFile request, CancellationToken cancellationToken)
    {
        var file = await store.GetFileAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PaymentBatchMapping.NotFound();
        return new PaymentBatchFileDto(file.FileName, PaymentBatchMapping.ContentType(file.FileName), file.Sha256, Convert.ToBase64String(file.Content));
    }
}

/// <summary>
/// Un lot deschis de OP-uri ale aceluiasi cont (F2 3.6 si 4.2 pasul 4): lista inlocuieste OP-urile lotului; intra doar OP-uri validate,
/// neconfirmate, din ecranul Ordine de plata, care nu sunt in alt lot (51222).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderExport)]
[Audited("Fin.PaymentBatch.Save")]
public sealed record SavePaymentBatch(int? Id, int TreasuryAccountId, IReadOnlyList<int> OrderIds, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SavePaymentBatchValidator : AbstractValidator<SavePaymentBatch>
{
    public SavePaymentBatchValidator()
    {
        RuleFor(x => x.OrderIds).NotNull().Must(o => o.Count <= 2000 && o.Distinct().Count() == o.Count).WithMessage("Cel mult 2.000 de ordine distincte.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SavePaymentBatchHandler(IPaymentBatchStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SavePaymentBatch, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SavePaymentBatch request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveAsync(context.CompanyId, request.Id, request.TreasuryAccountId, request.OrderIds, request.RowVersion, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentBatch", saved.Id, new { request.TreasuryAccountId, Orders = request.OrderIds });
        return saved;
    }
}

/// <summary>
/// Exporta un lot (F2 3.6 si 4.2 pasul 4): toate OP-urile validate si, cu patru ochi activ, aprobate (51222); fisierul in formatul de plati
/// al profilului bancar (pain.001.001.03 / .09 sau CSV), pastrat cu hash; OrdinPlataExportat. Reexportul cere Fin.PaymentOrder.ReExport si
/// un motiv (51218) si pastreaza aceleasi EndToEndId, ca banca sa refuze dublura.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderExport)]
[Audited("Fin.PaymentBatch.Export")]
public sealed record ExportPaymentBatch(int Id, byte[] RowVersion, string? ReExportReason = null) : ICommand<PaymentBatchFileDto>;

internal sealed class ExportPaymentBatchHandler(IPaymentBatchStore store, IEnumerable<IPaymentFileWriter> writers, IFinanceSettings settings, IOutbox outbox,
    ICurrentContext context, IAuditScope audit, TimeProvider time) : ICommandHandler<ExportPaymentBatch, PaymentBatchFileDto>
{
    public async Task<PaymentBatchFileDto> Handle(ExportPaymentBatch request, CancellationToken cancellationToken)
    {
        var batch = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PaymentBatchMapping.NotFound();
        var reason = string.IsNullOrWhiteSpace(request.ReExportReason) ? null : request.ReExportReason.Trim();
        if (batch.Status == PaymentBatchStatus.Exported && !context.HasPermission(FinancePermissions.PaymentOrderReExport))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Reexportul unui lot cere dreptul de reexport.");
        }

        var format = batch.PaymentFormat ?? PaymentFileFormat.Pain001V03;
        var writer = writers.FirstOrDefault(w => w.Supports(format))
            ?? throw ErpException.Conflict("FIN_PAYMENT_BATCH_NO_FORMAT", "Profilul bancar al contului nu are export de plăți.");
        if (batch.AccountIban is null)
        {
            throw ErpException.Conflict("FIN_PAYMENT_BATCH_NO_FORMAT", "Contul lotului nu are IBAN.");
        }

        var created = TimeZoneInfo.ConvertTime(time.GetUtcNow(), BusinessDay.Zone).DateTime;
        var file = writer.Write(batch, format, created);
        var sha = BankStatementRules.Sha256Hex(file.Content);
        var requireApproval = await settings.RequireApprovalAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        await store.MarkExportedAsync(context.CompanyId, request.Id, request.RowVersion, format, file, sha, reason, requireApproval, context.UserId,
            cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(new OrdinPlataExportat(batch.Id, batch.TreasuryAccountId, batch.Orders.Sum(o => o.Amount),
            batch.Orders.Select(o => new OrdinPlataExportatLinie(o.Id, o.PartnerId, o.Amount)).ToList()) { CompanyId = context.CompanyId }, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PaymentBatch", batch.Id, new
        {
            Format = format.ToString(), file.FileName, FileSha256 = sha, batch.OrderCount, batch.TotalAmount, ReExportReason = reason,
            ReExport = batch.Status == PaymentBatchStatus.Exported,
        });
        return new PaymentBatchFileDto(file.FileName, file.ContentType, sha, Convert.ToBase64String(file.Content));
    }
}

/// <summary>
/// Anuleaza un lot (F2 4.7): exportat dar neincarcat la banca, cu confirmare explicita (motivul); OP-urile raman validate si pot intra intr-un
/// lot nou. Un lot cu OP-uri confirmate pe extras nu se anuleaza (51217).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderCancel)]
[Audited("Fin.PaymentBatch.Cancel")]
public sealed record CancelPaymentBatch(int Id, string Reason, byte[] RowVersion) : ICommand<byte[]>;

internal sealed class CancelPaymentBatchValidator : AbstractValidator<CancelPaymentBatch>
{
    public CancelPaymentBatchValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Anularea lotului cere un motiv (confirmați că fișierul nu a fost încărcat la bancă).")
            .MaximumLength(500);
}

internal sealed class CancelPaymentBatchHandler(IPaymentBatchStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<CancelPaymentBatch, byte[]>
{
    public async Task<byte[]> Handle(CancelPaymentBatch request, CancellationToken cancellationToken)
    {
        var version = await store.CancelAsync(context.CompanyId, request.Id, request.Reason.Trim(), request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PaymentBatch", request.Id, new { Reason = request.Reason.Trim() });
        return version;
    }
}
