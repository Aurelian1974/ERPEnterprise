using Shared.Kernel.Primitives;
using UUIDNext;

namespace Administration.Domain.Partners;

public sealed class Partner : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Cui { get; private set; }
    public string? RegistrationNumber { get; private set; }
    public string? LegalForm { get; private set; }
    public byte? PartnerTypeId { get; private set; }
    public bool IsVatPayer { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public bool IsActive { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTime? AnafVerifiedAt { get; private set; }

    private Partner(Guid id) : base(id) { }

    public static Partner Create(
        Guid tenantId,
        string code,
        string name,
        Guid createdBy,
        string? cui = null,
        string? registrationNumber = null,
        string? legalForm = null,
        byte? partnerTypeId = null,
        bool isVatPayer = false,
        string? phone = null,
        string? email = null,
        string? notes = null,
        DateTime? anafVerifiedAt = null)
    {
        return new Partner(Uuid.NewDatabaseFriendly(Database.SqlServer))
        {
            TenantId           = tenantId,
            Code               = code,
            Name               = name,
            Cui                = cui,
            RegistrationNumber = registrationNumber,
            LegalForm          = legalForm,
            PartnerTypeId      = partnerTypeId,
            IsVatPayer         = isVatPayer,
            Phone              = phone,
            Email              = email,
            IsActive           = true,
            Notes              = notes,
            CreatedAt          = DateTime.UtcNow,
            CreatedBy          = createdBy,
            AnafVerifiedAt     = anafVerifiedAt,
        };
    }

    public static Partner Rehydrate(
        Guid id, Guid tenantId, string code, string name,
        string? cui, string? registrationNumber, string? legalForm,
        byte? partnerTypeId, bool isVatPayer, string? phone, string? email,
        bool isActive, string? notes, DateTime createdAt, Guid createdBy,
        DateTime? updatedAt, Guid? updatedBy, DateTime? anafVerifiedAt = null)
    {
        return new Partner(id)
        {
            TenantId           = tenantId,
            Code               = code,
            Name               = name,
            Cui                = cui,
            RegistrationNumber = registrationNumber,
            LegalForm          = legalForm,
            PartnerTypeId      = partnerTypeId,
            IsVatPayer         = isVatPayer,
            Phone              = phone,
            Email              = email,
            IsActive           = isActive,
            Notes              = notes,
            CreatedAt          = createdAt,
            CreatedBy          = createdBy,
            UpdatedAt          = updatedAt,
            UpdatedBy          = updatedBy,
            AnafVerifiedAt     = anafVerifiedAt,
        };
    }

    public void Update(
        string code,
        string name,
        Guid updatedBy,
        string? cui = null,
        string? registrationNumber = null,
        string? legalForm = null,
        byte? partnerTypeId = null,
        bool isVatPayer = false,
        string? phone = null,
        string? email = null,
        bool isActive = true,
        string? notes = null)
    {
        Code               = code;
        Name               = name;
        Cui                = cui;
        RegistrationNumber = registrationNumber;
        LegalForm          = legalForm;
        PartnerTypeId      = partnerTypeId;
        IsVatPayer         = isVatPayer;
        Phone              = phone;
        Email              = email;
        IsActive           = isActive;
        Notes              = notes;
        UpdatedAt          = DateTime.UtcNow;
        UpdatedBy          = updatedBy;
    }

    public void ApplyAnafData(
        bool isVatPayer,
        string? nrRegCom,
        string? legalForm,
        string? phone,
        Guid updatedBy)
    {
        IsVatPayer         = isVatPayer;
        RegistrationNumber = nrRegCom   ?? RegistrationNumber;
        LegalForm          = legalForm  ?? LegalForm;
        Phone              = phone      ?? Phone;
        AnafVerifiedAt     = DateTime.UtcNow;
        UpdatedAt          = DateTime.UtcNow;
        UpdatedBy          = updatedBy;
    }
}
