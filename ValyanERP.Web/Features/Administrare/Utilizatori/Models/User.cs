using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.Utilizatori.Models;

public class User
{
    public Guid Id { get; set; }
    
    [Required]
    public Guid PersoanaId { get; set; }
    
    // View property (computed - from join with Persoane)
    public string? NumeComplet { get; set; }

    [Required]
    public string UserName { get; set; } = string.Empty;
    
    public string? NormalizedUserName { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    public string? NormalizedEmail { get; set; }
    public bool EmailConfirmed { get; set; }
    
    public string? PasswordHash { get; set; }
    public string? SecurityStamp { get; set; }
    public string? ConcurrencyStamp { get; set; }
    
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    
    public bool TwoFactorEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UserCreateDto : User
{
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    
    // Note: PasswordHash inherited from base User class
}
