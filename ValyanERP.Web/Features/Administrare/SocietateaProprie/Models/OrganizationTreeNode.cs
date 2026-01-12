namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

/// <summary>
/// Nod pentru reprezentare ierarhică în TreeView.
/// Poate fi Group, Company, WorkPlace sau Location.
/// </summary>
public class OrganizationTreeNode
{
    /// <summary>
    /// ID-ul nodului (string pentru binding în TreeView).
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// ID-ul părintelui.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Tipul nodului: GROUP, COMPANY, WORKPLACE, LOCATION.
    /// </summary>
    public string NodeType { get; set; } = string.Empty;

    /// <summary>
    /// Denumirea afișată.
    /// </summary>
    public string Denumire { get; set; } = string.Empty;

    /// <summary>
    /// Codul asociat (CUI, CodONRC, CodIntern).
    /// </summary>
    public string? Cod { get; set; }

    /// <summary>
    /// Subtipul nodului (Holding, Sucursală, Depozit, etc.).
    /// </summary>
    public string? TipNode { get; set; }

    /// <summary>
    /// Nivelul în ierarhie (0 = root).
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Starea activă.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Ordinea de sortare.
    /// </summary>
    public int SortOrder { get; set; }

    // Flags pentru locații

    /// <summary>
    /// Locația poate gestiona stoc.
    /// </summary>
    public bool HasStock { get; set; }

    /// <summary>
    /// Locația poate vinde.
    /// </summary>
    public bool CanSell { get; set; }

    /// <summary>
    /// Locația poate achiziționa.
    /// </summary>
    public bool CanPurchase { get; set; }

    // Pentru TreeView binding

    /// <summary>
    /// Starea de expandare.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Indică dacă nodul are copii.
    /// </summary>
    public bool HasChildren { get; set; }

    /// <summary>
    /// Lista nodurilor copil.
    /// </summary>
    public List<OrganizationTreeNode> Children { get; set; } = new();

    /// <summary>
    /// Returnează iconița Bootstrap pentru tipul nodului.
    /// </summary>
    public string Icon => NodeType switch
    {
        "GROUP" => "bi-building",
        "COMPANY" => "bi-bank",
        "WORKPLACE" => "bi-geo-alt-fill",
        "LOCATION" => GetLocationIcon(),
        _ => "bi-circle"
    };

    /// <summary>
    /// Returnează clasa CSS pentru stilizare.
    /// </summary>
    public string NodeClass => NodeType switch
    {
        "GROUP" => "node-group",
        "COMPANY" => "node-company",
        "WORKPLACE" => "node-workplace",
        "LOCATION" => "node-location",
        _ => ""
    };

    /// <summary>
    /// Returnează clasa CSS pentru badge.
    /// </summary>
    public string BadgeClass => HasStock || CanSell || CanPurchase
        ? "bg-success"
        : "bg-secondary";

    private string GetLocationIcon() => TipNode switch
    {
        "Depozit" => "bi-box-seam",
        "Magazin" => "bi-shop",
        "Birou" => "bi-briefcase",
        "Showroom" => "bi-display",
        "Teren" => "bi-tree",
        "Service" => "bi-wrench",
        "Parcare" => "bi-p-square",
        _ => "bi-pin-map"
    };
}
