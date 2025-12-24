using Microsoft.AspNetCore.Identity;

namespace ValyanERP.Web.Infrastructure.Identity;

/// <summary>
/// Custom role class for ASP.NET Core Identity with Dapper.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    
    public ApplicationRole(string roleName) : base(roleName)
    {
        NormalizedName = roleName.ToUpperInvariant();
    }
}
