using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Reporting;
using Erp.Finance.Domain;

namespace Erp.Finance.Application.Reporting;

public sealed record InventoryLine(string AccountCode, string AccountName, int? PartnerId, string? PartnerName, byte Side, decimal BookValue, decimal InventoryValue);

public sealed record InventoryLineDto(string AccountCode, string AccountName, int? PartnerId, string? PartnerName, byte Side, string BookValue, string InventoryValue,
    string Difference);

/// <summary>Registrul inventar al unui an (C5 3.4): final daca anul e inchis, cu elementele conturilor de bilant.</summary>
public sealed record InventoryRegisterDto(int Id, int Year, bool IsFinal, DateTime GeneratedAt, IReadOnlyList<InventoryLineDto> Lines);

/// <summary>Registrul inventar prin fin.usp_InventoryRegister_* (NET-06).</summary>
public interface IInventoryRegisterStore
{
    Task<IReadOnlyDictionary<int, string>> PartnerNamesAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

    Task<int> SaveAsync(int companyId, int year, IReadOnlyList<InventoryLine> lines, byte[] pdf, int userId, CancellationToken ct);

    Task<(InventoryRegisterDto Register, byte[]? Pdf)?> GetAsync(int companyId, int year, bool withPdf, CancellationToken ct);
}

/// <summary>PDF-ul registrului inventar (QuestPDF), cu firma, anul, conturile, elementele si totalurile.</summary>
public interface IInventoryRegisterPrinter
{
    byte[] Print(CompanySnapshotDto company, int year, bool isFinal, IReadOnlyList<InventoryLine> lines);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportInventoryRegister)]
public sealed record GetInventoryRegister(int Year) : IQuery<InventoryRegisterDto?>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportInventoryRegister)]
public sealed record DownloadInventoryRegister(int Year) : IQuery<Settlement.DownloadedFile>;

/// <summary>
/// Genereaza registrul inventar (C5 3.4): soldurile conturilor de bilant (clasele 1–5) la sfarsitul anului, pe partener, din balanta; valoarea de
/// inventar e deocamdata valoarea contabila (listele de inventariere ale Stocurilor si Mijloacelor fixe vin cu modulele lor).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportInventoryRegister)]
[Audited("Fin.InventoryRegister.Generate")]
public sealed record GenerateInventoryRegister(int Year) : ICommand<int>;

internal sealed class InventoryRegisterHandlers(IInventoryRegisterStore store, IInventoryRegisterPrinter printer, IBalanceQuery balances, IAccountingPeriodStore periods,
    ICompanyDirectory companies, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetInventoryRegister, InventoryRegisterDto?>, IQueryHandler<DownloadInventoryRegister, Settlement.DownloadedFile>,
    ICommandHandler<GenerateInventoryRegister, int>
{
    public async Task<InventoryRegisterDto?> Handle(GetInventoryRegister request, CancellationToken cancellationToken) =>
        (await store.GetAsync(context.CompanyId, request.Year, false, cancellationToken).ConfigureAwait(false))?.Register;

    public async Task<Settlement.DownloadedFile> Handle(DownloadInventoryRegister request, CancellationToken cancellationToken)
    {
        var r = await store.GetAsync(context.CompanyId, request.Year, true, cancellationToken).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_REPORT_NOT_FOUND", "Registrul inventar nu a fost generat.");
        return new Settlement.DownloadedFile($"Registru_inventar_{request.Year}.pdf", "application/pdf", r.Pdf ?? []);
    }

    public async Task<int> Handle(GenerateInventoryRegister request, CancellationToken cancellationToken)
    {
        var year = (await periods.ListAsync(context.CompanyId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(y => y.Year == request.Year)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_PERIOD_NOT_FOUND", "Anul financiar nu există.");
        var rows = (await balances.GetBalanceAsync(new BalanceRequest(context.CompanyId, null, year.StartDate, year.EndDate, 3, true, null, true, false, false),
                cancellationToken).ConfigureAwait(false))
            .Where(r => r.AccountCode[0] is >= '1' and <= '5' && r.ClosingDebit - r.ClosingCredit != 0).ToList();
        var names = await store.PartnerNamesAsync(rows.Where(r => r.PartnerId is not null).Select(r => r.PartnerId!.Value).Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);
        var lines = rows.OrderBy(r => r.AccountCode, StringComparer.Ordinal).Select(r =>
        {
            var net = r.ClosingDebit - r.ClosingCredit;
            return new InventoryLine(r.AccountCode, r.AccountName, r.PartnerId, r.PartnerId is { } p ? names.GetValueOrDefault(p) : null, net > 0 ? (byte)1 : (byte)2,
                Math.Abs(net), Math.Abs(net));
        }).ToList();
        var company = await companies.GetAtAsync(context.CompanyId, year.EndDate, cancellationToken).ConfigureAwait(false);
        var pdf = printer.Print(company, request.Year, year.Status >= 2, lines);
        var id = await store.SaveAsync(context.CompanyId, request.Year, lines, pdf, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.InventoryRegister", id, new { request.Year, Lines = lines.Count });
        return id;
    }
}
