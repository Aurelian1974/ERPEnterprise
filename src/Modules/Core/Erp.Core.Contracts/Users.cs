namespace Erp.Core.Contracts;

/// <summary>Un utilizator a fost dezactivat (manual, la expirarea accesului sau la incetarea contractului; 02.01, 5).</summary>
public sealed record UtilizatorDezactivat(int UserId, Guid SubjectId) : IntegrationEvent;

/// <summary>Starea contului de autentificare (ids), fara hash-uri sau chei, pentru tab-ul Securitate al fisei utilizatorului.</summary>
public sealed record IdentityAccountSession(Guid SessionId, DateTime CreatedAt, DateTime LastSeenAt, string? IpAddress, string? UserAgent);

/// <summary>
/// Administrarea conturilor de autentificare din schema ids (02.01, 3.4 si 11; 08, 8.7), folosita de administrarea utilizatorilor
/// din Nucleu. Implementarea e in Erp.Auth (hash-ul parolei si regulile ei); toate operatiile ruleaza in tranzactia comenzii
/// curente (IDbSession), impreuna cu utilizatorul din core si cu auditul. Parolele temporare se intorc o singura data, pentru
/// a fi aratate administratorului; nu se scriu in audit sau in jurnale (NET-18).
/// </summary>
public interface IIdentityAccountAdmin
{
    /// <summary>Contul nou; interactive = false (cont tehnic) il creeaza fara parola. Intoarce parola temporara sau null.</summary>
    Task<string?> CreateAsync(Guid subjectId, string userName, string? email, bool interactive, bool twoFactorRequired, int actorUserId,
        CancellationToken ct);

    /// <summary>
    /// Nume, email, stare si 2FA obligatoriu; invalidateSessions (dezactivare, nume schimbat) invalideaza cookie-urile si inchide
    /// sesiunile. false = utilizatorul nu are cont de autentificare (nimic de actualizat; contul se creeaza la resetarea parolei).
    /// </summary>
    Task<bool> UpdateAsync(Guid subjectId, string userName, string? email, bool isActive, bool twoFactorRequired, bool invalidateSessions,
        int actorUserId, CancellationToken ct);

    /// <summary>
    /// Parola temporara noua (schimbare obligatorie la autentificare), cont deblocat, sesiuni inchise. Daca utilizatorul nu are inca
    /// cont de autentificare, il creeaza cu aceasta parola. Intoarce parola.
    /// </summary>
    Task<string> ResetPasswordAsync(Guid subjectId, string userName, string? email, bool twoFactorRequired, int actorUserId, CancellationToken ct);

    Task UnlockAsync(Guid subjectId, int actorUserId, CancellationToken ct);

    /// <summary>Dezactiveaza aplicatia de autentificare si codurile de rezerva; utilizatorul o activeaza din nou la autentificare.</summary>
    Task ResetTwoFactorAsync(Guid subjectId, int actorUserId, CancellationToken ct);

    Task<IReadOnlyList<IdentityAccountSession>> GetSessionsAsync(Guid subjectId, CancellationToken ct);

    /// <summary>Inchide o sesiune a contului (sessionId) sau pe toate (null); cookie-urile sunt respinse in cel mult un minut.</summary>
    Task RevokeSessionsAsync(Guid subjectId, Guid? sessionId, int actorUserId, CancellationToken ct);
}
