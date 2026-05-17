namespace Administration.Domain.Partners;

public sealed class PartnerType
{
    public byte PartnerTypeId { get; init; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; private set; }
    public bool AffectsIssuedInvoices { get; private set; }
    public bool AffectsReceivedInvoices { get; private set; }
    public short SortOrder { get; private set; }
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = default!;
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = default!;
}
