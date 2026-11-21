using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.NumberSeries;

/// <summary>O regula de alegere a seriei (02.01, 3.6 punctul 16); PartnerScope: 1 Romania, 2 UE, 3 non-UE.</summary>
public sealed record NumberSeriesRuleDto(int Id, int CompanyId, string DocumentTypeCode, string DocumentTypeName, int Priority, int? UserId,
    string? UserName, int? BranchId, string? BranchName, int? LocationId, string? LocationName, byte? PartnerScope, string Prefix, bool IsActive,
    byte[] RowVersion);

/// <summary>Regulile firmei si utilizatorii ei (pentru conditia „utilizator”).</summary>
public sealed record NumberSeriesRulePageDto(IReadOnlyList<NumberSeriesRuleDto> Rules, IReadOnlyList<Partners.UserOptionDto> Users);

/// <summary>Accesul la date pentru reguli (core.usp_NumberSeriesRule_*, NET-06).</summary>
public interface INumberSeriesRuleStore
{
    Task<NumberSeriesRulePageDto> ListAsync(int companyId, CancellationToken ct);

    Task<SavedEntity> SaveAsync(SaveNumberSeriesRule rule, int userId, CancellationToken ct);
}

/// <summary>Regulile firmei curente (Configurare › Serii).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NumberSeriesManage)]
public sealed record ListNumberSeriesRules(int CompanyId) : IQuery<NumberSeriesRulePageDto>;

/// <summary>Regula de alegere a seriei; nu se sterge, se dezactiveaza.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NumberSeriesManage)]
[Audited("Core.NumberSeriesRule.Save")]
public sealed record SaveNumberSeriesRule(int? Id, int CompanyId, string DocumentTypeCode, int Priority, int? UserId, int? BranchId, int? LocationId,
    byte? PartnerScope, string Prefix, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>
/// Seriile din care utilizatorul poate alege pe un document al firmei curente si cea propusa (din reguli, pentru el); folosita de
/// ecranele documentelor. Orice utilizator autentificat: nu arata altceva decat prefixele si numerele urmatoare.
/// </summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetNumberSeriesOptions(string DocumentType, DateOnly? Date = null, int? BranchId = null, int? LocationId = null,
    byte? PartnerScope = null) : IQuery<NumberSeriesOptions>;

internal sealed class SaveNumberSeriesRuleValidator : AbstractValidator<SaveNumberSeriesRule>
{
    public SaveNumberSeriesRuleValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.DocumentTypeCode).NotEmpty().WithMessage("Alegeți tipul de document.");
        RuleFor(x => x.Priority).InclusiveBetween(1, 999).WithMessage("Ordinea e între 1 și 999.");
        RuleFor(x => x.Prefix).NotEmpty().WithMessage("Alegeți seria.").Matches("^[A-Za-z0-9]{1,20}$").WithMessage("Prefixul are 1–20 litere sau cifre.");
        RuleFor(x => x.PartnerScope).InclusiveBetween((byte)1, (byte)3).When(x => x.PartnerScope is not null).WithMessage("Tipul partenerului nu e valid.");
        RuleFor(x => x).Must(x => x.UserId is not null || x.BranchId is not null || x.LocationId is not null || x.PartnerScope is not null)
            .WithName("Conditions").WithMessage("Regula are cel puțin o condiție (utilizator, punct de lucru, locație sau tipul partenerului).");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class ListNumberSeriesRulesHandler(INumberSeriesRuleStore store, ICurrentContext context)
    : IQueryHandler<ListNumberSeriesRules, NumberSeriesRulePageDto>
{
    public Task<NumberSeriesRulePageDto> Handle(ListNumberSeriesRules request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        return store.ListAsync(request.CompanyId, cancellationToken);
    }
}

internal sealed class SaveNumberSeriesRuleHandler(INumberSeriesRuleStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveNumberSeriesRule, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveNumberSeriesRule request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.CompanyId);
        var rule = request with { Prefix = request.Prefix.Trim() };
        var saved = await store.SaveAsync(rule, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.NumberSeriesRule", saved.Id, new
        {
            rule.CompanyId, rule.DocumentTypeCode, rule.Priority, rule.UserId, rule.BranchId, rule.LocationId, rule.PartnerScope, rule.Prefix, rule.IsActive,
        });
        return saved;
    }
}

internal sealed class GetNumberSeriesOptionsHandler(INumberSeriesService series, ICurrentContext context, TimeProvider clock)
    : IQueryHandler<GetNumberSeriesOptions, NumberSeriesOptions>
{
    public Task<NumberSeriesOptions> Handle(GetNumberSeriesOptions request, CancellationToken cancellationToken)
    {
        if (context.CompanyId == 0)
        {
            return Task.FromResult(new NumberSeriesOptions([], null));
        }

        var date = request.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return series.GetOptionsAsync(context.CompanyId, request.DocumentType, date, request.BranchId,
            new NumberSeriesChoice(null, context.UserId, request.LocationId, (PartnerScope?)request.PartnerScope), cancellationToken);
    }
}
