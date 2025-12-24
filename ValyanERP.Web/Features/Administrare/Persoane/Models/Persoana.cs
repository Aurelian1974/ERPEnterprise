using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.Persoane.Models;

/// <summary>
/// Represents a person entity in the system.
/// </summary>
public class Persoana
{
    public Guid Id { get; set; }
    public string Nume { get; set; } = string.Empty;
    public string Prenume { get; set; } = string.Empty;
    public string? NumeComplet { get; set; }
    public string? CNP { get; set; }
    public DateTime? DataNasterii { get; set; }
    public string? Email { get; set; }
    public string? Telefon { get; set; }
    public string? Adresa { get; set; }
    public string? Oras { get; set; }
    public string? Judet { get; set; }
    public string? CodPostal { get; set; }
    public string? Tara { get; set; } = "Romania";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByUserName { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string? UpdatedByUserName { get; set; }
}

/// <summary>
/// DTO for creating a new person.
/// </summary>
public class CreatePersoanaDto
{
    [Required(ErrorMessage = "Numele este obligatoriu")]
    [StringLength(100, ErrorMessage = "Numele nu poate depăși 100 caractere")]
    public string Nume { get; set; } = string.Empty;

    [Required(ErrorMessage = "Prenumele este obligatoriu")]
    [StringLength(100, ErrorMessage = "Prenumele nu poate depăși 100 caractere")]
    public string Prenume { get; set; } = string.Empty;

    [StringLength(13, MinimumLength = 13, ErrorMessage = "CNP-ul trebuie să aibă exact 13 caractere")]
    [RegularExpression(@"^\d{13}$", ErrorMessage = "CNP-ul trebuie să conțină doar cifre")]
    public string? CNP { get; set; }

    public DateTime? DataNasterii { get; set; }

    [EmailAddress(ErrorMessage = "Adresa de email nu este validă")]
    [StringLength(256, ErrorMessage = "Email-ul nu poate depăși 256 caractere")]
    public string? Email { get; set; }

    [Phone(ErrorMessage = "Numărul de telefon nu este valid")]
    [StringLength(20, ErrorMessage = "Telefonul nu poate depăși 20 caractere")]
    public string? Telefon { get; set; }

    [StringLength(500, ErrorMessage = "Adresa nu poate depăși 500 caractere")]
    public string? Adresa { get; set; }

    [StringLength(100, ErrorMessage = "Orașul nu poate depăși 100 caractere")]
    public string? Oras { get; set; }

    [StringLength(100, ErrorMessage = "Județul nu poate depăși 100 caractere")]
    public string? Judet { get; set; }

    [StringLength(10, ErrorMessage = "Codul poștal nu poate depăși 10 caractere")]
    public string? CodPostal { get; set; }

    [StringLength(100, ErrorMessage = "Țara nu poate depăși 100 caractere")]
    public string? Tara { get; set; } = "Romania";
}

/// <summary>
/// DTO for updating an existing person.
/// </summary>
public class UpdatePersoanaDto : CreatePersoanaDto
{
    public Guid Id { get; set; }
    public bool IsActive { get; set; } = true;
}
