using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ValyanERP.Web.Infrastructure.Identity;

/// <summary>
/// Custom user class for ASP.NET Core Identity with Dapper.
/// Each user MUST be associated with a Persoana (mandatory relationship).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Foreign key to Persoane table. Required - every user must have an associated person.
    /// </summary>
    [Required(ErrorMessage = "Persoana este obligatorie")]
    public Guid PersoanaId { get; set; }
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
