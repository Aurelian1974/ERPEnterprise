namespace Erp.Auth;

/// <summary>
/// Contul de autentificare (ids.UserAccount, 08 8.7) impreuna cu starea utilizatorului din Nucleu (core.v_UserLoginStatus).
/// Id-ul vazut de ASP.NET Core Identity este SubjectId (claim NameIdentifier), legatura cu core.[User].
/// </summary>
public sealed class UserAccount
{
    public int Id { get; set; }

    public Guid SubjectId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string NormalizedUserName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? PasswordHash { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public bool MustChangePassword { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public bool TwoFactorEnabled { get; set; }

    public bool TwoFactorRequired { get; set; }

    public byte[]? AuthenticatorKeyEncrypted { get; set; }

    public bool LockoutEnabled { get; set; }

    public DateTime? LockoutEndAt { get; set; }

    public int AccessFailedCount { get; set; }

    /// <summary>Contul ids e activ, iar utilizatorul din Nucleu e activ si (extern) neexpirat.</summary>
    public bool CanSignIn { get; set; }

    public int? CoreUserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsSystem { get; set; }
}
