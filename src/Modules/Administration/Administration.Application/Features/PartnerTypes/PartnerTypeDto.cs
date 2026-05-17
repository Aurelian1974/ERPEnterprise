using Administration.Domain.Partners;

namespace Administration.Application.Features.PartnerTypes;

public sealed record PartnerTypeDto(
    byte PartnerTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    bool AffectsIssuedInvoices,
    bool AffectsReceivedInvoices,
    short SortOrder,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime UpdatedAt,
    string UpdatedBy
)
{
    public static PartnerTypeDto FromDomain(PartnerType pt) => new(
        pt.PartnerTypeId,
        pt.Code,
        pt.Name,
        pt.Description,
        pt.IsSystem,
        pt.IsActive,
        pt.AffectsIssuedInvoices,
        pt.AffectsReceivedInvoices,
        pt.SortOrder,
        pt.CreatedAt,
        pt.CreatedBy,
        pt.UpdatedAt,
        pt.UpdatedBy
    );
}
