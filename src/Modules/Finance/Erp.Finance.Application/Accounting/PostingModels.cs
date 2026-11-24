using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain.Accounting;
using FluentValidation;

namespace Erp.Finance.Application.Accounting;

public sealed record PostingModelItemDto(int Id, string EventType, string Name, string JournalCode, bool IsSystem, bool IsActive, int VariantCount);

public sealed record PostingModelLineDto(int LineNumber, string Component, string? FilterVatCategory, string? FilterOperationTypeCode, byte Side, short Sign,
    string AccountKey, string? DefaultAccountCode, byte CopyDimensions, byte GroupBy, string? DescriptionTemplate);

public sealed record PostingVariantDto(int Id, int Priority, string Name, string? RequiredFlags, string? ForbiddenFlags, byte? PartnerKind, string? DocumentType,
    DateOnly ValidFrom, DateOnly? ValidTo, bool IsActive, byte[] RowVersion, IReadOnlyList<PostingModelLineDto> Lines);

public sealed record AccountKeyDto(string Code, string Name, string Chain);

/// <summary>Un model de contare cu variantele si liniile lor (macheta c2-03) si cheile de cont disponibile.</summary>
public sealed record PostingModelDto(int Id, string EventType, string Name, string JournalCode, string JournalName, bool IsSystem, bool IsActive,
    IReadOnlyList<PostingVariantDto> Variants, IReadOnlyList<AccountKeyDto> AccountKeys);

public sealed record PostingModelLineInput(string Component, string? FilterVatCategory, string? FilterOperationTypeCode, byte Side, short Sign, string AccountKey,
    string? DefaultAccountCode, byte CopyDimensions = Dimensions.All, byte GroupBy = 1, string? DescriptionTemplate = null);

/// <summary>Linia simularii: contul, partea, suma, partenerul, centrul de cost si de unde vine contul.</summary>
public sealed record SimulatedLineDto(string AccountCode, byte Side, string AmountRon, int? PartnerId, int? CostCenterId, ResolvedFrom ResolvedFrom);

/// <summary>Simularea contarii unui document real (C2 4.2): liniile pe care le-ar scrie azi contarea, sau motivul de coada.</summary>
public sealed record PostingSimulationDto(string EventType, int? VariantId, IReadOnlyList<SimulatedLineDto> Lines, PostingFailureReason? Failure, string? Details);

/// <summary>Modelele prin fin.usp_PostingModel_*, fin.usp_PostingModelVariant_SaveLines, fin.usp_JournalEntrySource_Get (NET-06).</summary>
public interface IPostingModelStore
{
    Task<IReadOnlyList<PostingModelItemDto>> ListAsync(CancellationToken ct);

    Task<PostingModelDto?> GetAsync(int id, CancellationToken ct);

    Task<byte[]> SaveLinesAsync(int variantId, IReadOnlyList<PostingModelLineInput> lines, byte[] rowVersion, int userId, CancellationToken ct);

    Task<(string EventType, AccountingPayload Payload)?> SourceAsync(int companyId, long entryId, CancellationToken ct);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingModelView)]
public sealed record GetPostingModels : IQuery<IReadOnlyList<PostingModelItemDto>>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingModelView)]
public sealed record GetPostingModel(int Id) : IQuery<PostingModelDto>;

/// <summary>
/// Simularea contarii unei note automate existente (C2 4.2, C2-A10): contractul ei pastrat, trecut prin aceeasi functie ca la contarea reala, cu
/// modelul si conturile de azi. Fara scriere.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingModelView)]
public sealed record SimulatePosting(long EntryId) : IQuery<PostingSimulationDto>;

/// <summary>Inlocuieste liniile unei variante (C2 3.2). Notele scrise raman; recontarea le reface la cerere (4.6).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingModelManage)]
[Audited("Fin.PostingModel.SaveLines")]
public sealed record SavePostingVariantLines(int VariantId, IReadOnlyList<PostingModelLineInput> Lines, byte[] RowVersion) : ICommand<byte[]>;

internal sealed class SavePostingVariantLinesValidator : AbstractValidator<SavePostingVariantLines>
{
    public SavePostingVariantLinesValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= 50);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Component).NotEmpty().MaximumLength(40);
            l.RuleFor(x => x.AccountKey).NotEmpty().MaximumLength(40);
            l.RuleFor(x => x.Side).InclusiveBetween((byte)1, (byte)2);
            l.RuleFor(x => x.Sign).Must(s => s is 1 or -1);
            l.RuleFor(x => x.GroupBy).InclusiveBetween((byte)0, (byte)2);
            l.RuleFor(x => x.CopyDimensions).LessThanOrEqualTo(Dimensions.All);
            l.RuleFor(x => x.DefaultAccountCode).NotEmpty().When(x => x.AccountKey == "Fixed").WithMessage("Cheia Fixed cere contul implicit.");
        });
    }
}

internal sealed class PostingModelHandlers(IPostingModelStore store, PostingService posting, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetPostingModels, IReadOnlyList<PostingModelItemDto>>, IQueryHandler<GetPostingModel, PostingModelDto>,
    IQueryHandler<SimulatePosting, PostingSimulationDto>, ICommandHandler<SavePostingVariantLines, byte[]>
{
    public Task<IReadOnlyList<PostingModelItemDto>> Handle(GetPostingModels request, CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    public async Task<PostingModelDto> Handle(GetPostingModel request, CancellationToken cancellationToken) =>
        await store.GetAsync(request.Id, cancellationToken).ConfigureAwait(false)
        ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_POSTING_MODEL_MISSING", "Modelul nu există.");

    public async Task<PostingSimulationDto> Handle(SimulatePosting request, CancellationToken cancellationToken)
    {
        var source = await store.SourceAsync(context.CompanyId, request.EntryId, cancellationToken).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_ENTRY_NOT_FOUND", "Nota nu e automată sau nu are contractul de contare păstrat.");
        var result = await posting.PreviewAsync(context.CompanyId, source.EventType, source.Payload, cancellationToken).ConfigureAwait(false);
        return new PostingSimulationDto(source.EventType, result.VariantId, result.Lines.Select(l => new SimulatedLineDto(l.AccountCode, l.Side,
            Domain.Money.Format(l.AmountRon), l.PartnerId, l.CostCenterId, l.ResolvedFrom)).ToList(), result.Failure, result.Details);
    }

    public async Task<byte[]> Handle(SavePostingVariantLines request, CancellationToken cancellationToken)
    {
        var version = await store.SaveLinesAsync(request.VariantId, request.Lines, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PostingModelVariant", request.VariantId, request.Lines);
        return version;
    }
}
