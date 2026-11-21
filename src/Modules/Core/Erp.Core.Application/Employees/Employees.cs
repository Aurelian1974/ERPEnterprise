using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Employees;

/// <summary>Un rand din lista angajatilor; UserId / UserName = contul de utilizator legat, daca exista.</summary>
public sealed record EmployeeListItemDto(int Id, int CompanyId, string CompanyName, int? BranchId, string? BranchName, string EmployeeNo,
    string FirstName, string LastName, string? Department, string? JobTitle, string? WorkEmail, DateOnly? HireDate, DateOnly? LeaveDate,
    bool IsActive, int? UserId, string? UserName);

public sealed record EmployeePageDto(IReadOnlyList<EmployeeListItemDto> Items, int Total, int ActiveCount);

/// <summary>Fisa minima de angajat (02.01, 3.4), cu contul de utilizator legat.</summary>
public sealed record EmployeeDetailDto(int Id, int CompanyId, string CompanyName, int? BranchId, string? BranchName, string EmployeeNo,
    string FirstName, string LastName, string? Department, string? JobTitle, string? WorkEmail, string? Phone, DateOnly? HireDate,
    DateOnly? LeaveDate, bool IsActive, int? UserId, string? UserName, DateTime CreatedAt, DateTime LastChangedAt, string ModifiedByName,
    byte[] RowVersion);

public sealed record BranchOptionDto(int Id, int CompanyId, string Name);

public sealed record EmployeeLookupsDto(IReadOnlyList<CompanyOptionDto> Companies, IReadOnlyList<BranchOptionDto> Branches);

public sealed record EmployeeSearchCriteria(string? Search, int? CompanyId, bool? IsActive, bool? WithoutUser, int Offset, int PageSize);

/// <summary>Contul de utilizator legat de fisa salvata (sincronizarea emailului in contul de autentificare).</summary>
public sealed record EmployeeSaveResult(int Id, byte[] RowVersion, int? LinkedUserId, Guid? LinkedSubjectId, string? LinkedUserName,
    string? LinkedEmail, bool? LinkedIsActive);

/// <summary>Accesul la date pentru angajati (implementat in Infrastructure, NET-06).</summary>
public interface IEmployeeStore
{
    Task<EmployeePageDto> SearchAsync(EmployeeSearchCriteria criteria, CancellationToken ct);

    Task<EmployeeDetailDto?> GetAsync(int id, CancellationToken ct);

    Task<EmployeeLookupsDto> GetLookupsAsync(CancellationToken ct);

    Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct);

    Task<EmployeeSaveResult> SaveAsync(SaveEmployee command, int userId, CancellationToken ct);
}

/// <summary>Lista angajatilor; WithoutUser = true: doar cei fara cont de utilizator (alegerea angajatului in fisa utilizatorului).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.EmployeeView)]
public sealed record SearchEmployees(string? Search = null, int? CompanyId = null, bool? IsActive = null, bool? WithoutUser = null, int Page = 1,
    int PageSize = 50) : IQuery<EmployeePageDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.EmployeeView)]
public sealed record GetEmployee(int Id) : IQuery<EmployeeDetailDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.EmployeeView)]
public sealed record GetEmployeeLookups : IQuery<EmployeeLookupsDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.EmployeeView)]
public sealed record GetEmployeeHistory(int Id) : IQuery<IReadOnlyList<PartnerHistoryEntryDto>>;

/// <summary>
/// Creare sau modificare a fisei minime de angajat (02.01, 3.4). Contul de utilizator legat preia numele si emailul; data incetarii
/// opreste autentificarea de la acea data, iar jobul zilnic dezactiveaza contul.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.EmployeeManage)]
[Audited("Core.Employee.Save")]
public sealed record SaveEmployee(int? Id, int CompanyId, int? BranchId, string EmployeeNo, string FirstName, string LastName, string? Department,
    string? JobTitle, string? WorkEmail, string? Phone, DateOnly? HireDate, DateOnly? LeaveDate, bool IsActive, byte[]? RowVersion)
    : ICommand<SavedEntity>;

internal sealed class SaveEmployeeValidator : AbstractValidator<SaveEmployee>
{
    public SaveEmployeeValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0).WithMessage("Alegeți firma angajatoare.");
        RuleFor(x => x.EmployeeNo).NotEmpty().WithMessage("Marca este obligatorie.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9._/-]{0,19}$").WithMessage("Marca are 1–20 caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Prenumele este obligatoriu.").MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Numele este obligatoriu.").MaximumLength(100);
        RuleFor(x => x.Department).MaximumLength(100).WithMessage("Departamentul are cel mult 100 de caractere.");
        RuleFor(x => x.JobTitle).MaximumLength(100).WithMessage("Funcția are cel mult 100 de caractere.");
        RuleFor(x => x.LeaveDate).GreaterThanOrEqualTo(x => x.HireDate).When(x => x.HireDate is not null && x.LeaveDate is not null)
            .WithMessage("Data încetării nu poate fi înaintea datei angajării.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        RuleFor(x => x.WorkEmail).MaximumLength(254).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.WorkEmail))
            .WithMessage("E-mailul nu este valid.");
        RuleFor(x => x.Phone).Matches(@"^\+?[0-9][0-9 .()/\-]{5,28}$").When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Telefonul are 6–30 de caractere: cifre, spații și + ( ) - . /.");
    }
}

internal sealed class SaveEmployeeHandler(IEmployeeStore store, IIdentityAccountAdmin identity, IUserStore users, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<SaveEmployee, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveEmployee request, CancellationToken cancellationToken)
    {
        var command = request with
        {
            EmployeeNo = request.EmployeeNo.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Department = UserRules.Clean(request.Department),
            JobTitle = UserRules.Clean(request.JobTitle),
            WorkEmail = UserRules.Clean(request.WorkEmail)?.ToLowerInvariant(),
            Phone = UserRules.Clean(request.Phone),
        };
        var saved = await store.SaveAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);

        // Emailul contului de autentificare urmeaza fisa (folosit la „Am uitat parola”).
        if (saved is { LinkedUserId: { } userId, LinkedSubjectId: { } subjectId, LinkedUserName: { } userName, LinkedIsActive: { } active })
        {
            var account = await users.GetAccountAsync(userId, cancellationToken).ConfigureAwait(false);
            var twoFactor = account is not null && UserRules.TwoFactorRequired(true, account.IsSystem, null, account.IsAdmin);
            await identity.UpdateAsync(subjectId, userName, saved.LinkedEmail, active, twoFactor, invalidateSessions: false, context.UserId,
                cancellationToken).ConfigureAwait(false);
        }

        // Fara date personale in audit (NET-18): doar datele organizatorice ale fisei.
        audit.Describe("Core.Employee", saved.Id, new
        {
            command.CompanyId, command.BranchId, command.EmployeeNo, command.Department, command.JobTitle, command.HireDate, command.LeaveDate,
            command.IsActive,
        });
        return new SavedEntity(saved.Id, saved.RowVersion);
    }
}

internal sealed class SearchEmployeesHandler(IEmployeeStore store) : IQueryHandler<SearchEmployees, EmployeePageDto>
{
    public Task<EmployeePageDto> Handle(SearchEmployees request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null
            : request.Search.Trim().Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);
        return store.SearchAsync(new EmployeeSearchCriteria(search, request.CompanyId, request.IsActive, request.WithoutUser, (page - 1) * pageSize,
            pageSize), cancellationToken);
    }
}

internal sealed class GetEmployeeHandler(IEmployeeStore store) : IQueryHandler<GetEmployee, EmployeeDetailDto>
{
    public async Task<EmployeeDetailDto> Handle(GetEmployee request, CancellationToken cancellationToken) =>
        await store.GetAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Angajatul nu există.");
}

internal sealed class GetEmployeeLookupsHandler(IEmployeeStore store) : IQueryHandler<GetEmployeeLookups, EmployeeLookupsDto>
{
    public Task<EmployeeLookupsDto> Handle(GetEmployeeLookups request, CancellationToken cancellationToken) => store.GetLookupsAsync(cancellationToken);
}

internal sealed class GetEmployeeHistoryHandler(IEmployeeStore store) : IQueryHandler<GetEmployeeHistory, IReadOnlyList<PartnerHistoryEntryDto>>
{
    public Task<IReadOnlyList<PartnerHistoryEntryDto>> Handle(GetEmployeeHistory request, CancellationToken cancellationToken) =>
        store.GetHistoryAsync(request.Id, cancellationToken);
}
