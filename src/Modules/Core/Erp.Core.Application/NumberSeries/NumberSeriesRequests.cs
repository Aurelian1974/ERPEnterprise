using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.NumberSeries;

/// <summary>Seriile firmei curente (macheta 07c), optional pe un an.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NumberSeriesManage)]
public sealed record ListNumberSeries(int CompanyId, short? Year) : IQuery<IReadOnlyList<NumberSeriesDto>>;

/// <summary>Creare sau modificare de serie (02.01, 3.5 si 4.2): o serie fiscala folosita nu-si mai schimba numerotarea.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NumberSeriesManage)]
[Audited("Core.NumberSeries.Save")]
public sealed record SaveNumberSeries(int? Id, int CompanyId, int? BranchId, string DocumentTypeCode, string Prefix, short Year, long NextNumber,
    byte Padding, bool IsFiscal, bool IsDefault, bool AutoRollover, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>Inchiderea unei serii: nu mai aloca numere; trece in audit (macheta 07c).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NumberSeriesManage)]
[Audited("Core.NumberSeries.Close")]
public sealed record CloseNumberSeries(int Id, int CompanyId, byte[] RowVersion) : ICommand<SavedEntity>;

internal sealed class SaveNumberSeriesValidator : AbstractValidator<SaveNumberSeries>
{
    public SaveNumberSeriesValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.DocumentTypeCode).NotEmpty().WithMessage("Alegeți tipul de document.");
        RuleFor(x => x.Prefix).NotEmpty().WithMessage("Prefixul este obligatoriu.")
            .Matches("^[A-Za-z0-9]{1,20}$").WithMessage("Prefixul are 1–20 litere sau cifre.");
        RuleFor(x => x.Year).InclusiveBetween((short)2000, (short)2999).WithMessage("Anul nu este valid.");
        RuleFor(x => x.NextNumber).GreaterThanOrEqualTo(1).WithMessage("Următorul număr este cel puțin 1.");
        RuleFor(x => x.Padding).InclusiveBetween((byte)1, (byte)12).WithMessage("Lungimea numărului este între 1 și 12 cifre.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class ListNumberSeriesHandler(IOrganizationStore store, ICurrentContext context)
    : IQueryHandler<ListNumberSeries, IReadOnlyList<NumberSeriesDto>>
{
    public Task<IReadOnlyList<NumberSeriesDto>> Handle(ListNumberSeries request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        return store.ListNumberSeriesAsync(request.CompanyId, request.Year, cancellationToken);
    }
}

internal sealed class SaveNumberSeriesHandler(IOrganizationStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveNumberSeries, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveNumberSeries request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        var saved = await store.SaveNumberSeriesAsync(request, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.NumberSeries", saved.Id, new
        {
            request.CompanyId, request.BranchId, request.DocumentTypeCode, request.Prefix, request.Year, request.NextNumber, request.Padding,
            request.IsFiscal, request.IsDefault, request.AutoRollover,
        });
        return saved;
    }
}

internal sealed class CloseNumberSeriesHandler(IOrganizationStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<CloseNumberSeries, SavedEntity>
{
    public async Task<SavedEntity> Handle(CloseNumberSeries request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        var saved = await store.CloseNumberSeriesAsync(request, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.NumberSeries", saved.Id);
        return saved;
    }
}
