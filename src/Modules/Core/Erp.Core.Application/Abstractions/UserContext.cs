namespace Erp.Core.Application.Abstractions;

/// <summary>Utilizatorul activ si permisiunile lui pe firma efectiva (ceruta sau ultima folosita; 0 = niciuna).</summary>
public sealed record UserContextData(
    int UserId,
    string UserName,
    string DisplayName,
    string Language,
    int CompanyId,
    IReadOnlyCollection<string> Permissions);

/// <summary>Incarcarea contextului utilizatorului autentificat, o data per cerere.</summary>
public interface IUserContextLoader
{
    /// <summary>null = utilizator inexistent, inactiv sau expirat (cookie-ul nu mai e acceptat, 4.6).</summary>
    Task<UserContextData?> LoadAsync(Guid subjectId, int? companyId, CancellationToken ct);
}
