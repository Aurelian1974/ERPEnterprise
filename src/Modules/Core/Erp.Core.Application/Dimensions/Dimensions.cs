using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Dimensions;

/// <summary>Un tip de dimensiune (IsSystem = livrat; tipurile clientului se adauga din ecran, 3.6 punctul 15).</summary>
public sealed record DimensionTypeDto(string Code, string Name, int ValueCount, int SortOrder, bool IsSystem, bool IsActive, byte[] RowVersion);

/// <summary>O valoare in lista ecranului (macheta 07k): firma, parintele, responsabilul, valabilitatea si copiii activi.</summary>
public sealed record DimensionValueItemDto(int Id, int CompanyId, string CompanyName, string DimensionType, string Code, string Name, int? ParentId,
    string? ParentCode, string? ParentName, int? ResponsibleUserId, string? ResponsibleName, DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive,
    int ActiveChildren, byte[] RowVersion);

/// <summary>Configurare › Dimensiuni: tipurile cu numarul de valori, valorile tipului ales, firmele si responsabilii posibili.</summary>
public sealed record DimensionPageDto(IReadOnlyList<DimensionTypeDto> Types, IReadOnlyList<DimensionValueItemDto> Values,
    IReadOnlyList<CompanyOptionDto> Companies, IReadOnlyList<UserOptionDto> Users);

public sealed record DimensionSearchCriteria(int UserId, string DimensionType, int? CompanyId, string? Search, bool IncludeInactive);

public sealed record DimensionValueRecord(int? Id, int CompanyId, string DimensionType, string Code, string Name, int? ParentId, int? ResponsibleUserId,
    DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive, byte[]? RowVersion);

/// <summary>Accesul la date pentru dimensiuni (core.usp_Dimension*, NET-06).</summary>
public interface IDimensionStore
{
    Task<DimensionPageDto> ListAsync(DimensionSearchCriteria criteria, CancellationToken ct);

    Task<SavedEntity> SaveAsync(DimensionValueRecord value, int userId, CancellationToken ct);
}

/// <summary>Lista ecranului Dimensiuni; CompanyId null = toate firmele pe care utilizatorul are dreptul pe dimensiuni.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.DimensionManage)]
public sealed record GetDimensions(string Type = DimensionTypes.CostCenter, int? CompanyId = null, string? Search = null, bool IncludeInactive = false)
    : IQuery<DimensionPageDto>;

/// <summary>
/// Valoarea unei dimensiuni (macheta 07k). O valoare noua intra in firma aleasa (una pe care utilizatorul are dreptul); firma si tipul
/// nu se schimba dupa creare. Dezactivarea inlocuieste stergerea.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.DimensionManage)]
[Audited("Core.Dimension.Save")]
public sealed record SaveDimensionValue(int? Id, int CompanyId, string DimensionType, string Code, string Name, int? ParentId, int? ResponsibleUserId,
    DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

internal sealed class SaveDimensionValueValidator : AbstractValidator<SaveDimensionValue>
{
    public SaveDimensionValueValidator()
    {
        RuleFor(x => x.DimensionType).Must(DimensionTypeCode.IsValid).WithMessage("Alegeți tipul de dimensiune.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$").WithMessage("Codul are 1–30 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200).WithMessage("Denumirea are cel mult 200 de caractere.");
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidFrom is not null && x.ValidTo is not null)
            .WithMessage("Valabilitatea nu se poate încheia înainte să înceapă.");
        RuleFor(x => x.CompanyId).GreaterThan(0).When(x => x.Id is null).WithMessage("Alegeți firma.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class GetDimensionsHandler(IDimensionStore store, ICurrentContext context) : IQueryHandler<GetDimensions, DimensionPageDto>
{
    public Task<DimensionPageDto> Handle(GetDimensions request, CancellationToken cancellationToken)
    {
        var type = DimensionTypeCode.IsValid(request.Type) ? request.Type : DimensionTypes.CostCenter;
        var search = string.IsNullOrWhiteSpace(request.Search) ? null
            : request.Search.Trim().Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal);
        return store.ListAsync(new DimensionSearchCriteria(context.UserId, type, request.CompanyId, search, request.IncludeInactive), cancellationToken);
    }
}

internal sealed class SaveDimensionValueHandler(IDimensionStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveDimensionValue, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveDimensionValue request, CancellationToken cancellationToken)
    {
        var record = new DimensionValueRecord(request.Id, request.CompanyId, request.DimensionType, request.Code.Trim(), request.Name.Trim(),
            request.ParentId, request.ResponsibleUserId, request.ValidFrom, request.ValidTo, request.IsActive, request.RowVersion);
        var saved = await store.SaveAsync(record, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.DimensionValue", saved.Id, new
        {
            record.CompanyId, record.DimensionType, record.Code, record.Name, record.ParentId, record.ResponsibleUserId, record.ValidFrom,
            record.ValidTo, record.IsActive,
        });
        return saved;
    }
}
