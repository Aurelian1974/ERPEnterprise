using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Declarations;
using FluentValidation;

namespace Erp.Finance.Application.Declarations;

public sealed record TaxReturnLineDto(string RowCode, string ColumnCode, string Label, bool IsManual, string CalculatedValue, string? ManualValue, string? ManualReason);

/// <summary>Un rand al comparatiei cu P300 (macheta c4-02): valoarea declarata, cea precompletata, diferenta, marcajul peste prag, explicatia.</summary>
public sealed record TaxComparisonDto(string RowCode, string ColumnCode, string Declared, string Precompleted, string Difference, bool IsOverThreshold,
    string? Explanation);

public sealed record PrecompletedValue(string RowCode, string ColumnCode, string Value);

/// <summary>O linie pe partener a D394 / D390 (macheta c4-02).</summary>
public sealed record PartnerLineDto(string Section, int? PartnerId, string? PartnerFiscalCode, string? PartnerCountryCode, string? PartnerName, decimal VatRatePercent,
    int Documents, string BaseRon, string VatRon, string? Issue);

/// <summary>Calculul unei declaratii: randurile, amprenta datelor si, la declaratiile informative, liniile pe partener.</summary>
public sealed record TaxReturnCalculation(TaxComputation Computation, int Hash, IReadOnlyList<PartnerLine>? PartnerLines);

public sealed record TaxReturnFileDto(int Id, byte Kind, string FileName, long Size, DateTime CreatedAt);

/// <summary>O declaratie (machetele c4-01, c4-02). Status: 1 ciorna, 2 verificata, 3 generata, 4 depusa, 5 inlocuita.</summary>
public sealed record TaxReturnDto(int Id, string FormCode, string FormName, DateOnly PeriodFrom, DateOnly PeriodTo, byte Kind, byte Status, bool NeedsReview,
    DateOnly? DueDate, DateTime? CalculatedAt, DateTime? GeneratedAt, DateTime? SubmittedAt, string? RegistrationNumber, string AmountDue, string AmountRefundable,
    DateTime? PrecompletedAt, byte[] RowVersion, IReadOnlyList<TaxReturnLineDto> Lines, IReadOnlyList<TaxReturnFileDto> Files,
    IReadOnlyList<TaxComparisonDto> Comparison, IReadOnlyList<PartnerLineDto> PartnerLines);

/// <summary>Un termen al calendarului fiscal (macheta c4-01), cu declaratia perioadei si starea ei.</summary>
public sealed record TaxCalendarItemDto(string FormCode, string FormName, DateOnly PeriodFrom, DateOnly PeriodTo, DateOnly DueDate, int? ReturnId, byte? Status,
    bool NeedsReview, string? AmountDue);

public sealed record TaxReturnState(int Id, string FormCode, DateOnly PeriodFrom, DateOnly PeriodTo, byte Status, decimal AmountDue, DateOnly? DueDate, byte[] RowVersion);

public sealed record TaxFile(string FileName, byte[] Content);

/// <summary>Declaratiile prin fin.usp_TaxReturn_*, fin.usp_TaxCalendar_*, fin.usp_VatRegister_Summary (NET-06).</summary>
public interface ITaxReturnStore
{
    Task<(IReadOnlyList<VatSummaryRow> Rows, int Hash)> VatSummaryAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<PartnerVatRow>> VatByPartnerAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task SavePartnerLinesAsync(int companyId, int id, IReadOnlyList<PartnerLine> lines, CancellationToken ct);

    Task<int> SaveAsync(int companyId, string formCode, DateOnly from, DateOnly to, DateOnly? dueDate, TaxComputation computation, int hash, int userId,
        CancellationToken ct);

    Task<TaxReturnDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<TaxReturnState> TransitionAsync(int companyId, int id, byte[] rowVersion, string action, int? hash, string? registrationNumber, int userId,
        CancellationToken ct);

    Task SaveComparisonAsync(int companyId, int id, IReadOnlyList<PrecompletedDifference> rows, bool imported, int userId, CancellationToken ct);

    Task ExplainAsync(int companyId, int id, string rowCode, string columnCode, string explanation, int userId, CancellationToken ct);

    Task SaveFileAsync(int companyId, int id, byte kind, string fileName, byte[] content, int userId, CancellationToken ct);

    Task<TaxFile?> GetFileAsync(int companyId, int fileId, CancellationToken ct);

    Task<IReadOnlyList<(int Id, string FormCode, DateOnly From, DateOnly To, int? Hash)>> MarkReviewAsync(int companyId, DateOnly from, DateOnly to, bool reopened,
        CancellationToken ct);

    Task ClearReviewAsync(int companyId, int id, CancellationToken ct);

    Task SaveCalendarAsync(int companyId, IReadOnlyList<TaxDeadline> items, CancellationToken ct);

    Task<IReadOnlyList<TaxCalendarItemDto>> CalendarAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Fisierul final al unei declaratii (C4.3+): XML-ul formularului ANAF. Lipsind, se pastreaza instantaneul calculului (JSON).</summary>
public interface ITaxReturnFileGenerator
{
    string FormCode { get; }

    Task<TaxFile> GenerateAsync(int companyId, TaxReturnDto taxReturn, CancellationToken ct);
}

/// <summary>
/// Declaratiile (C4 3.3, 4.1): calculul ciornei din registru (D300 acum; celelalte formulare vin cu pasii lor), verificarea, generarea doar pe
/// lunile inchise si cu datele neschimbate de la calcul (decizia 3), depunerea cu numarul de inregistrare si recipisa (decizia 1), obligatia de
/// plata in prognoza F5, marcajul „De verificat” la redeschidere (C4-A11).
/// </summary>
public sealed class TaxReturnService(ITaxReturnStore store, ISaftStore saft, IEnumerable<ITaxReturnFileGenerator> generators,
    IEnumerable<IStreamingTaxFileGenerator> streaming, IOutbox outbox)
{
    public static readonly string[] SupportedForms = ["D300", "D394", "D390", "D406"];

    public async Task<TaxReturnCalculation> ComputeAsync(int companyId, string formCode, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!SupportedForms.Contains(formCode))
        {
            throw new ErpException(ErpErrorKind.Conflict, "FIN_TAX_FORM_VERSION_MISSING", $"Formularul {formCode} nu se calculează încă.");
        }

        if (formCode == "D406")
        {
            var summary = await saft.SummaryAsync(companyId, from, to, ct).ConfigureAwait(false);
            return new TaxReturnCalculation(SaftComputation.Lines(summary), summary.Hash, null);
        }

        var (rows, hash) = await store.VatSummaryAsync(companyId, from, to, ct).ConfigureAwait(false);
        if (formCode == "D300")
        {
            return new TaxReturnCalculation(D300.Compute(rows), hash, null);
        }

        var byPartner = await store.VatByPartnerAsync(companyId, from, to, ct).ConfigureAwait(false);
        var informative = formCode == "D394" ? InformativeReturns.ComputeD394(byPartner) : InformativeReturns.ComputeD390(byPartner);
        return new TaxReturnCalculation(informative.Summary, hash, informative.Lines);
    }

    /// <summary>Compara randurile calculate ale declaratiei cu valorile P300 (RO e-TVA, decizia 5).</summary>
    public static IReadOnlyList<PrecompletedDifference> Compare(IEnumerable<TaxReturnLineDto> lines, IReadOnlyDictionary<(string Row, string Column), decimal> precompleted)
    {
        var declared = PrecompletedVatComparison.Declared(lines.Select(l => new TaxLine(l.RowCode, l.ColumnCode, l.Label, 0,
            Money.ParseOptional(l.ManualValue ?? l.CalculatedValue) ?? 0)));
        return PrecompletedVatComparison.Compare(declared, precompleted);
    }

    /// <summary>Scrie si salveaza fisierul final al declaratiei: in flux pentru D406, altfel din generatorul formularului (JSON in lipsa lui).</summary>
    public async Task<string> WriteFileAsync(int companyId, TaxReturnDto taxReturn, int userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taxReturn);
        if (streaming.FirstOrDefault(g => g.FormCode == taxReturn.FormCode) is { } writer)
        {
            await writer.GenerateAsync(companyId, taxReturn, userId, ct).ConfigureAwait(false);
            return taxReturn.FormCode;
        }

        var file = await GenerateAsync(companyId, taxReturn, ct).ConfigureAwait(false);
        await store.SaveFileAsync(companyId, taxReturn.Id, 1, file.FileName, file.Content, userId, ct).ConfigureAwait(false);
        return file.FileName;
    }

    public async Task<TaxFile> GenerateAsync(int companyId, TaxReturnDto taxReturn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taxReturn);
        var generator = generators.FirstOrDefault(g => g.FormCode == taxReturn.FormCode);
        if (generator is not null)
        {
            return await generator.GenerateAsync(companyId, taxReturn, ct).ConfigureAwait(false);
        }

        var snapshot = JsonSerializer.SerializeToUtf8Bytes(new { taxReturn.FormCode, taxReturn.PeriodFrom, taxReturn.PeriodTo, taxReturn.AmountDue, taxReturn.AmountRefundable,
            Lines = taxReturn.Lines.Select(l => new { l.RowCode, l.ColumnCode, Value = l.ManualValue ?? l.CalculatedValue }) },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return new TaxFile($"{taxReturn.FormCode}_{taxReturn.PeriodFrom:yyyy-MM}.json", snapshot);
    }

    /// <summary>La reinchiderea unei luni (C4-A11): declaratiile „De verificat” ale caror date nu s-au schimbat isi pierd marcajul.</summary>
    public async Task RecheckAsync(int companyId, DateOnly from, DateOnly to, bool reopened, CancellationToken ct)
    {
        var marked = await store.MarkReviewAsync(companyId, from, to, reopened, ct).ConfigureAwait(false);
        if (reopened)
        {
            return;
        }

        foreach (var r in marked.Where(m => SupportedForms.Contains(m.FormCode)))
        {
            var calculation = await ComputeAsync(companyId, r.FormCode, r.From, r.To, ct).ConfigureAwait(false);
            if (calculation.Hash == r.Hash)
            {
                await store.ClearReviewAsync(companyId, r.Id, ct).ConfigureAwait(false);
            }
        }
    }

    public Task PublishSubmittedAsync(int companyId, TaxReturnState state, string registrationNumber, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        return outbox.EnqueueAsync(new Contracts.Declarations.DeclaratieDepusa(state.Id, state.FormCode, state.PeriodFrom, state.PeriodTo, registrationNumber,
            state.AmountDue, state.DueDate)
        {
            CompanyId = companyId,
        }, ct);
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxView)]
public sealed record GetTaxReturn(int Id) : IQuery<TaxReturnDto>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxView)]
public sealed record GetTaxCalendar(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<TaxCalendarItemDto>>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxView)]
public sealed record DownloadTaxReturnFile(int FileId) : IQuery<Settlement.DownloadedFile>;

/// <summary>Genereaza calendarul fiscal al unui an (C4 3.7) din anul financiar si periodicitatea de TVA a firmei.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxPrepare)]
public sealed record GenerateTaxCalendar(int Year) : ICommand<int>;

/// <summary>Calculeaza (sau recalculeaza) ciorna unei declaratii pentru perioada (C4 4.1 pasul 1); randurile calculate nu se editeaza.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxPrepare)]
[Audited("Fin.Tax.Calculate")]
public sealed record CalculateTaxReturn(string FormCode, DateOnly PeriodFrom, DateOnly PeriodTo) : ICommand<int>;

internal sealed class CalculateTaxReturnValidator : AbstractValidator<CalculateTaxReturn>
{
    public CalculateTaxReturnValidator()
    {
        RuleFor(x => x.FormCode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.PeriodTo).GreaterThanOrEqualTo(x => x.PeriodFrom);
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxPrepare)]
[Audited("Fin.Tax.Verify")]
public sealed record VerifyTaxReturn(int Id, byte[] RowVersion) : ICommand<byte[]>;

/// <summary>Genereaza fisierul declaratiei (C4 4.1 pasul 4): doar pe lunile inchise (51943) si cu datele neschimbate de la calcul (51944).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxGenerate)]
[Audited("Fin.Tax.Generate")]
public sealed record GenerateTaxReturn(int Id, byte[] RowVersion) : ICommand<byte[]>;

/// <summary>Inregistreaza depunerea (C4 4.1 pasul 5, decizia 1): numarul de inregistrare si, optional, recipisa; obligatia de plata merge in F5.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxSubmit)]
[Audited("Fin.Tax.Submit")]
public sealed record SubmitTaxReturn(int Id, byte[] RowVersion, string RegistrationNumber, string? ReceiptFileName = null, string? ReceiptBase64 = null)
    : ICommand<byte[]>;

internal sealed class SubmitTaxReturnValidator : AbstractValidator<SubmitTaxReturn>
{
    public SubmitTaxReturnValidator() => RuleFor(x => x.RegistrationNumber).NotEmpty().WithMessage("Scrieți numărul de înregistrare din recipisă.").MaximumLength(50);
}

/// <summary>Preia decontul precompletat P300 (C4 3.4): valorile pe randurile formularului, din SPV prin Integrari sau incarcate de contabil.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxPrepare)]
[Audited("Fin.Tax.Precompleted")]
public sealed record ImportPrecompletedVat(int Id, IReadOnlyList<PrecompletedValue> Rows) : ICommand<IReadOnlyList<TaxComparisonDto>>;

internal sealed class ImportPrecompletedVatValidator : AbstractValidator<ImportPrecompletedVat>
{
    public ImportPrecompletedVatValidator() => RuleForEach(x => x.Rows).Must(r => !string.IsNullOrWhiteSpace(r.RowCode) && Money.TryParse(r.Value, out _))
        .WithMessage("Fiecare rând P300 are cod și valoare cu cel mult două zecimale.");
}

/// <summary>Explica o diferenta fata de P300 (decizia 5); explicatia ramane la recalculare.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxPrepare)]
[Audited("Fin.Tax.Explain")]
public sealed record ExplainTaxDifference(int Id, string RowCode, string ColumnCode, string Explanation) : ICommand<bool>;

internal sealed class ExplainTaxDifferenceValidator : AbstractValidator<ExplainTaxDifference>
{
    public ExplainTaxDifferenceValidator() => RuleFor(x => x.Explanation).NotEmpty().WithMessage("Scrieți explicația diferenței.").MaximumLength(1000);
}

internal sealed class TaxReturnHandlers(TaxReturnService service, ITaxReturnStore store, IAccountingPeriodStore periods, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetTaxReturn, TaxReturnDto>, IQueryHandler<GetTaxCalendar, IReadOnlyList<TaxCalendarItemDto>>, IQueryHandler<DownloadTaxReturnFile, Settlement.DownloadedFile>,
    ICommandHandler<GenerateTaxCalendar, int>, ICommandHandler<CalculateTaxReturn, int>, ICommandHandler<VerifyTaxReturn, byte[]>,
    ICommandHandler<GenerateTaxReturn, byte[]>, ICommandHandler<SubmitTaxReturn, byte[]>,
    ICommandHandler<ImportPrecompletedVat, IReadOnlyList<TaxComparisonDto>>, ICommandHandler<ExplainTaxDifference, bool>
{
    public async Task<IReadOnlyList<TaxComparisonDto>> Handle(ImportPrecompletedVat request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        var precompleted = request.Rows.GroupBy(r => (r.RowCode.Trim(), r.ColumnCode.Trim()))
            .ToDictionary(g => g.Key, g => g.Sum(r => Money.ParseOptional(r.Value) ?? 0));
        var rows = TaxReturnService.Compare(current.Lines, precompleted);
        await store.SaveComparisonAsync(context.CompanyId, request.Id, rows, true, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.TaxReturn", request.Id, new { Rows = request.Rows.Count, Over = rows.Count(r => r.IsOverThreshold) });
        return (await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false))!.Comparison;
    }

    public async Task<bool> Handle(ExplainTaxDifference request, CancellationToken cancellationToken)
    {
        await store.ExplainAsync(context.CompanyId, request.Id, request.RowCode, request.ColumnCode, request.Explanation.Trim(), context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.TaxReturn", request.Id, new { request.RowCode, request.ColumnCode });
        return true;
    }

    public async Task<TaxReturnDto> Handle(GetTaxReturn request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();

    public Task<IReadOnlyList<TaxCalendarItemDto>> Handle(GetTaxCalendar request, CancellationToken cancellationToken) =>
        store.CalendarAsync(context.CompanyId, request.From, request.To, cancellationToken);

    public async Task<Settlement.DownloadedFile> Handle(DownloadTaxReturnFile request, CancellationToken cancellationToken)
    {
        var f = await store.GetFileAsync(context.CompanyId, request.FileId, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        var type = f.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? "application/xml"
            : f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/json";
        return new Settlement.DownloadedFile(f.FileName, type, f.Content);
    }

    public async Task<int> Handle(GenerateTaxCalendar request, CancellationToken cancellationToken)
    {
        var year = (await periods.ListAsync(context.CompanyId, cancellationToken).ConfigureAwait(false)).SingleOrDefault(y => y.Year == request.Year)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_PERIOD_NOT_FOUND", "Anul financiar nu există.");
        var items = TaxCalendar.ForYear(year.StartDate, year.EndDate, year.VatPeriodicity);
        await store.SaveCalendarAsync(context.CompanyId, items, cancellationToken).ConfigureAwait(false);
        return items.Count;
    }

    public async Task<int> Handle(CalculateTaxReturn request, CancellationToken cancellationToken)
    {
        var calculation = await service.ComputeAsync(context.CompanyId, request.FormCode, request.PeriodFrom, request.PeriodTo, cancellationToken)
            .ConfigureAwait(false);
        var (computation, hash) = (calculation.Computation, calculation.Hash);
        var due = (await store.CalendarAsync(context.CompanyId, request.PeriodTo, request.PeriodTo.AddYears(1), cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.FormCode == request.FormCode && c.PeriodFrom == request.PeriodFrom)?.DueDate;
        var id = await store.SaveAsync(context.CompanyId, request.FormCode, request.PeriodFrom, request.PeriodTo, due, computation, hash, context.UserId,
            cancellationToken).ConfigureAwait(false);
        if (calculation.PartnerLines is { } partnerLines)
        {
            await store.SavePartnerLinesAsync(context.CompanyId, id, partnerLines, cancellationToken).ConfigureAwait(false);
        }

        var saved = await store.GetAsync(context.CompanyId, id, cancellationToken).ConfigureAwait(false);
        if (saved is { PrecompletedAt: not null })
        {
            var precompleted = saved.Comparison.ToDictionary(c => (c.RowCode, c.ColumnCode), c => Money.ParseOptional(c.Precompleted) ?? 0);
            await store.SaveComparisonAsync(context.CompanyId, id, TaxReturnService.Compare(saved.Lines, precompleted).Where(r => r.Precompleted != 0 || r.Declared != 0)
                .ToList(), false, context.UserId, cancellationToken).ConfigureAwait(false);
        }

        audit.Describe("Fin.TaxReturn", id, new { request.FormCode, request.PeriodFrom, computation.AmountDue, computation.AmountRefundable });
        return id;
    }

    public async Task<byte[]> Handle(VerifyTaxReturn request, CancellationToken cancellationToken) =>
        (await store.TransitionAsync(context.CompanyId, request.Id, request.RowVersion, "Verify", null, null, context.UserId, cancellationToken).ConfigureAwait(false))
        .RowVersion;

    public async Task<byte[]> Handle(GenerateTaxReturn request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        var hash = (await service.ComputeAsync(context.CompanyId, current.FormCode, current.PeriodFrom, current.PeriodTo, cancellationToken).ConfigureAwait(false)).Hash;
        var state = await store.TransitionAsync(context.CompanyId, request.Id, request.RowVersion, "Generate", hash, null, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        var fileName = await service.WriteFileAsync(context.CompanyId, current, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.TaxReturn", request.Id, new { FileName = fileName });
        return state.RowVersion;
    }

    public async Task<byte[]> Handle(SubmitTaxReturn request, CancellationToken cancellationToken)
    {
        var state = await store.TransitionAsync(context.CompanyId, request.Id, request.RowVersion, "Submit", null, request.RegistrationNumber.Trim(), context.UserId,
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(request.ReceiptBase64))
        {
            await store.SaveFileAsync(context.CompanyId, request.Id, 4, request.ReceiptFileName ?? "recipisa.pdf", Convert.FromBase64String(request.ReceiptBase64),
                context.UserId, cancellationToken).ConfigureAwait(false);
        }

        await service.PublishSubmittedAsync(context.CompanyId, state, request.RegistrationNumber.Trim(), cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.TaxReturn", request.Id, new { request.RegistrationNumber });
        return state.RowVersion;
    }

    private static ErpException NotFound() => new(ErpErrorKind.NotFound, "FIN_TAX_RETURN_NOT_FOUND", "Declarația nu există.");
}
