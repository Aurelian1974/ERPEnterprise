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
