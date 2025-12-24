namespace ValyanERP.Web.Features.Administrare.Persoane.Models;

/// <summary>
/// Represents a person entity in the system.
/// </summary>
public class Persoana
{
    public int Id { get; set; }
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
    public string Nume { get; set; } = string.Empty;
    public string Prenume { get; set; } = string.Empty;
    public string? CNP { get; set; }
    public DateTime? DataNasterii { get; set; }
    public string? Email { get; set; }
    public string? Telefon { get; set; }
    public string? Adresa { get; set; }
    public string? Oras { get; set; }
    public string? Judet { get; set; }
    public string? CodPostal { get; set; }
    public string? Tara { get; set; } = "Romania";
}

/// <summary>
/// DTO for updating an existing person.
/// </summary>
public class UpdatePersoanaDto : CreatePersoanaDto
{
    public int Id { get; set; }
    public bool IsActive { get; set; } = true;
}
