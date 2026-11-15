using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Application.Me;

/// <summary>Profilul si permisiunile utilizatorului curent pe firma curenta (antetul aplicatiei, meniul dupa permisiuni, UI-07).</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetCurrentUser : IQuery<CurrentUserDto>;

/// <param name="Modules">Modulele active din licenta, ordonate (meniul arata doar aceste module).</param>
/// <param name="LicenseState">Valid, ExpiringSoon, Grace sau ReadOnly (02.01, 4.4).</param>
/// <param name="Companies">Firmele active la care utilizatorul are un rol (selectorul de firma); CompanyId e firma curenta, 0 = niciuna.</param>
public sealed record CurrentUserDto(int UserId, string UserName, string DisplayName, string Language, int CompanyId,
    IReadOnlyList<string> Permissions, IReadOnlyList<string> Modules, string LicenseState, IReadOnlyList<CompanySummaryDto> Companies);

/// <summary>Datele utilizatorului autentificat, incarcate o data per cerere.</summary>
public interface ICurrentUserProfile
{
    string UserName { get; }

    string DisplayName { get; }

    string Language { get; }

    IReadOnlyCollection<string> Permissions { get; }
}

internal sealed class GetCurrentUserHandler(ICurrentContext context, ICurrentUserProfile profile, ILicenseInfo license, IOrganizationStore organization)
    : IQueryHandler<GetCurrentUser, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(GetCurrentUser request, CancellationToken cancellationToken) =>
        new(context.UserId, profile.UserName, profile.DisplayName, profile.Language, context.CompanyId,
            [.. profile.Permissions.Order(StringComparer.Ordinal)], [.. license.ActiveModules.Order(StringComparer.Ordinal)],
            license.State.ToString(), await organization.GetUserCompaniesAsync(context.UserId, cancellationToken).ConfigureAwait(false));
}
