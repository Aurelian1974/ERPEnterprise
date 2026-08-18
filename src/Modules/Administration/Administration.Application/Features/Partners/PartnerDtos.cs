namespace Administration.Application.Features.Partners;

public sealed record PartnerListItemDto(
    Guid Id,
    string Code,
    string Name,
    string? Cui,
    bool IsActive,
    int TotalCount
);

public sealed record PartnerDetailDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string? Cui,
    string? RegistrationNumber,
    string? LegalForm,
    byte? PartnerTypeId,
    string? PartnerTypeName,
    bool IsVatPayer,
    string? Phone,
    string? Email,
    bool IsActive,
    string? Notes,
    DateTime CreatedAt,
    Guid CreatedBy,
    DateTime? UpdatedAt,
    Guid? UpdatedBy,
    DateTime? AnafVerifiedAt,
    IReadOnlyList<PartnerAddressDto> Addresses,
    IReadOnlyList<PartnerContactDto> Contacts,
    IReadOnlyList<PartnerBankAccountDto> BankAccounts
);

public sealed record PartnerAddressDto(
    long Id,
    string AddressType,
    string Street,
    string? StreetNumber,
    string? Block,
    string? Staircase,
    string? Floor,
    string? Apartment,
    string? Building,
    string City,
    string? County,
    string? PostalCode,
    string Country,
    bool IsPrimary
);

public sealed record PartnerContactDto(
    long Id,
    string FullName,
    string? Position,
    string? Phone,
    string? Email,
    bool IsPrimary
);

public sealed record PartnerBankAccountDto(
    long Id,
    string Iban,
    string BankName,
    string Currency,
    bool IsDefault
);
