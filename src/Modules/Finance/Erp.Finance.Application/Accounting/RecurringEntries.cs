using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounting;
using FluentValidation;

namespace Erp.Finance.Application.Accounting;

/// <summary>Un sablon de nota recurenta (C2 3.8). Periodicity: luni intre doua note (1, 3, 6, 12).</summary>
public sealed record RecurringTemplateDto(int Id, string Name, int JournalId, string JournalCode, byte Periodicity, DateOnly NextDate, DateOnly? EndDate,
    string? Description, IReadOnlyList<JournalLineInput> Lines, bool IsActive, long? LastEntryId, byte[] RowVersion);

/// <summary>Sabloanele de note recurente prin fin.usp_RecurringEntryTemplate_* (NET-06).</summary>
public interface IRecurringEntryStore
{
    Task<(int Id, byte[] RowVersion)> SaveAsync(int? id, int companyId, string name, int journalId, byte periodicity, DateOnly nextDate, DateOnly? endDate,
        string? description, string lines, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<RecurringTemplateDto>> ListAsync(int companyId, DateOnly? dueOn, CancellationToken ct);

    Task SetActiveAsync(int companyId, int id, bool isActive, byte[] rowVersion, int userId, CancellationToken ct);

    Task<bool> AdvanceAsync(int companyId, int id, DateOnly generatedFor, long entryId, CancellationToken ct);
}

public static class RecurringLines
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<JournalLineInput> lines) => JsonSerializer.Serialize(lines, Json);

    public static IReadOnlyList<JournalLineInput> Parse(string json) => JsonSerializer.Deserialize<List<JournalLineInput>>(json, Json) ?? [];
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.JournalEntryView)]
public sealed record GetRecurringTemplates : IQuery<IReadOnlyList<RecurringTemplateDto>>;

internal sealed class GetRecurringTemplatesHandler(IRecurringEntryStore store, ICurrentContext context)
    : IQueryHandler<GetRecurringTemplates, IReadOnlyList<RecurringTemplateDto>>
{
    public Task<IReadOnlyList<RecurringTemplateDto>> Handle(GetRecurringTemplates request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, null, cancellationToken);
}

/// <summary>Salveaza un sablon de nota recurenta (C2 3.8): jurnalul, periodicitatea, prima data, data de sfarsit si liniile cu sume fixe.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ManualEntryCreate)]
[Audited("Fin.RecurringEntry.Save")]
public sealed record SaveRecurringTemplate(int? Id, string Name, int JournalId, byte Periodicity, DateOnly NextDate, DateOnly? EndDate, string? Description,
    IReadOnlyList<JournalLineInput> Lines, byte[]? RowVersion = null) : ICommand<int>;

internal sealed class SaveRecurringTemplateValidator : AbstractValidator<SaveRecurringTemplate>
{
    public SaveRecurringTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(400);
        RuleFor(x => x.Periodicity).Must(p => p is 1 or 3 or 6 or 12).WithMessage("Periodicitatea e lunară, trimestrială, semestrială sau anuală.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.NextDate).When(x => x.EndDate is not null);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Adăugați cel puțin o linie.").Must(l => l.Count <= 500);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Side).InclusiveBetween((byte)1, (byte)2);
            l.RuleFor(x => x.AmountRon).Must(a => Domain.Money.ParseOptional(a) is { } v && v != 0).WithMessage("Suma liniei e diferită de zero.");
        });
        RuleFor(x => x.Lines).Must(l => l.Where(x => x.Side == 1).Sum(x => Domain.Money.ParseOptional(x.AmountRon) ?? 0)
                == l.Where(x => x.Side == 2).Sum(x => Domain.Money.ParseOptional(x.AmountRon) ?? 0))
            .WithMessage("Șablonul nu e echilibrat: totalul debit diferă de totalul credit.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null);
    }
}

internal sealed class SaveRecurringTemplateHandler(IRecurringEntryStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveRecurringTemplate, int>
{
    public async Task<int> Handle(SaveRecurringTemplate request, CancellationToken cancellationToken)
    {
        var (id, _) = await store.SaveAsync(request.Id, context.CompanyId, request.Name.Trim(), request.JournalId, request.Periodicity, request.NextDate,
            request.EndDate, request.Description?.Trim(), RecurringLines.Serialize(request.Lines), request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.RecurringEntryTemplate", id, request with { RowVersion = null });
        return id;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ManualEntryCreate)]
[Audited("Fin.RecurringEntry.SetActive")]
public sealed record SetRecurringTemplateActive(int Id, bool IsActive, byte[] RowVersion) : ICommand<bool>;

internal sealed class SetRecurringTemplateActiveHandler(IRecurringEntryStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetRecurringTemplateActive, bool>
{
    public async Task<bool> Handle(SetRecurringTemplateActive request, CancellationToken cancellationToken)
    {
        await store.SetActiveAsync(context.CompanyId, request.Id, request.IsActive, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.RecurringEntryTemplate", request.Id, new { request.IsActive });
        return true;
    }
}

/// <summary>
/// Genereaza ciornele scadente ale sabloanelor active pana la data aleasa (implicit azi; C2 3.8): o ciorna pe fiecare perioada ramasa in urma
/// (cel mult 24 pe sablon intr-o rulare), cu justificativul „sablon #Id”. Ciornele se valideaza de om, ca orice nota manuala.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ManualEntryCreate)]
[Audited("Fin.RecurringEntry.Generate")]
public sealed record GenerateRecurringEntries(DateOnly? OnDate = null) : ICommand<int>;

internal sealed class GenerateRecurringEntriesHandler(IRecurringEntryStore store, IJournalEntryStore entries, ICurrentContext context, TimeProvider time)
    : ICommandHandler<GenerateRecurringEntries, int>
{
    public async Task<int> Handle(GenerateRecurringEntries request, CancellationToken cancellationToken)
    {
        var today = Treasury.BusinessDay.Today(time);
        var onDate = request.OnDate ?? today;
        var created = 0;
        foreach (var t in await store.ListAsync(context.CompanyId, onDate, cancellationToken).ConfigureAwait(false))
        {
            var due = t.NextDate;
            var lines = t.Lines.Select(l => new JournalLineRecord(l.AccountId, l.Side, Domain.Money.ParseOptional(l.AmountRon) ?? 0, null, null, l.PartnerId,
                l.EmployeeId, l.CostCenterId, l.ProjectId, l.WarehouseId, l.SiteId, l.Description)).ToList();
            for (var i = 0; i < 24 && due <= onDate && (t.EndDate is null || due <= t.EndDate); i++)
            {
                var draft = await entries.SaveDraftAsync(null, context.CompanyId, t.JournalId, today, due, EntrySourceKind.Manual, t.Description ?? t.Name,
                    $"Șablon #{t.Id}", lines, null, context.UserId, cancellationToken).ConfigureAwait(false);
                if (!await store.AdvanceAsync(context.CompanyId, t.Id, due, draft.Id, cancellationToken).ConfigureAwait(false))
                {
                    throw new ErpException(ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT", "Șablonul a fost generat între timp de altcineva.");
                }

                created++;
                due = due == new DateOnly(due.Year, due.Month, DateTime.DaysInMonth(due.Year, due.Month))
                    ? EndOfMonth(due.AddMonths(t.Periodicity)) : due.AddMonths(t.Periodicity);
            }
        }

        return created;
    }

    private static DateOnly EndOfMonth(DateOnly d) => new(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
}
