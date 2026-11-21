namespace Erp.Core.Contracts;

/// <summary>Tipurile fixe de dimensiuni (02.01, 3.5; macheta 07k).</summary>
public static class DimensionTypes
{
    public const string CostCenter = "COST_CENTER";
    public const string Project = "PROJECT";
    public const string Warehouse = "WAREHOUSE";
    public const string Site = "SITE";

    public static readonly IReadOnlyList<string> All = [CostCenter, Project, Warehouse, Site];
}

/// <summary>O valoare de dimensiune (centru de cost, proiect, gestiune, santier) a unei firme.</summary>
public sealed record DimensionValueDto(int Id, int CompanyId, string DimensionType, string Code, string Name, int? ParentId, DateOnly? ValidFrom,
    DateOnly? ValidTo, bool IsActive);

/// <summary>
/// Dimensiunile pentru module (02.01, 6): citirea unei valori si crearea ei dupa cod, daca lipseste (Stocuri pentru gestiuni,
/// Constructii pentru santiere), in tranzactia comenzii modulului.
/// </summary>
public interface IDimensionLookup
{
    Task<DimensionValueDto?> GetAsync(int dimensionValueId, CancellationToken ct);

    Task<int> EnsureAsync(int companyId, string dimensionType, string code, string name, CancellationToken ct);
}

/// <summary>Regula unei dimensiuni implicite (02.01, 3.6 punctul 14): optionala (valoare propusa), obligatorie sau fixa.</summary>
public enum DimensionPosting : byte
{
    Optional = 1,
    Mandatory = 2,
    Fixed = 3,
}

/// <summary>Sursa unei dimensiuni implicite: partenerul, articolul sau locatia documentului.</summary>
public enum DimensionSource : byte
{
    Partner = 1,
    Item = 2,
    Location = 3,
}

/// <summary>Dimensiunea propusa pe document: valoarea (sau null), regula cea mai stricta si sursa valorii.</summary>
public sealed record DimensionDefault(string DimensionType, int? ValueId, DimensionPosting Posting, DimensionSource Source);

/// <summary>
/// O problema la validarea dimensiunilor unui document. Code: CORE_DIMENSION_REQUIRED (lipseste o valoare obligatorie),
/// CORE_DIMENSION_FIXED (alta valoare decat cea fixa), CORE_DIMENSION_CONFLICT (doua surse cer valori fixe diferite),
/// CORE_DIMENSION_COMBINATION_BLOCKED (combinatie blocata).
/// </summary>
public sealed record DimensionIssue(string DimensionType, string Code, string Message);

/// <summary>
/// Regulile dimensiunilor pentru module (02.01, 6): valorile propuse pe un document din partener, articol si locatie (in aceasta
/// ordine de prioritate: articolul, partenerul, locatia) si validarea valorilor alese, inclusiv combinatiile blocate ale firmei.
/// </summary>
public interface IDimensionRules
{
    Task<IReadOnlyList<DimensionDefault>> GetDefaultsAsync(int companyId, int? partnerId, int? itemId, int? locationId, CancellationToken ct);

    /// <param name="values">Valorile documentului (sau ale liniei), pe tip de dimensiune; null = necompletat.</param>
    Task<IReadOnlyList<DimensionIssue>> ValidateAsync(int companyId, IReadOnlyDictionary<string, int?> values, int? partnerId, int? itemId,
        int? locationId, CancellationToken ct);
}
