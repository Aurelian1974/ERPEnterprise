using Erp.Core.Contracts;
using Erp.Inventory.Domain.Lots;
using FluentValidation;

namespace Erp.Inventory.Application.Lots;

/// <summary>O serie (macheta s4-04): bucata, starea, gestiunea sau clientul, garantia.</summary>
public sealed record SerialItemDto(int SerialId, string SerialNo, int ItemVariantId, string VariantCode, string VariantName, SerialStatus Status, int? WarehouseId,
    string? WarehouseCode, int? LotId, string? LotCode, DateOnly? SoldAt, int? SoldToPartnerId, string? SoldToName, DateOnly? WarrantyUntil, bool IsCapturedAtIssue);

public sealed record SerialIdentifierDto(string TypeCode, string TypeName, string Value);

public sealed record SerialMovementDto(long MovementId, DateOnly MovementDate, string MovementTypeCode, string WarehouseCode, string QtyBase, string SourceDocumentType,
    string? SourceDocumentNumber, int? PartnerId);

/// <summary>Fisa seriei: bucata, identificatorii, miscarile.</summary>
public sealed record SerialDto(SerialItemDto Serial, IReadOnlyList<SerialIdentifierDto> Identifiers, IReadOnlyList<SerialMovementDto> Movements);

public sealed record SerialIdentifierRuleDto(int Id, int? ItemId, int? ItemGroupId, string TypeCode, int SortOrder, bool RequiredAtReceipt, bool RequiredAtIssue,
    bool IsUnique, bool OnDocuments);

/// <summary>Accesul la date pentru serii (inv.usp_Serial_*, inv.usp_SerialIdentifier_Edit, inv.usp_ItemSerialIdentifierRule_Save).</summary>
public interface ISerialStore
{
    Task<IReadOnlyList<SerialInfoDto>> FindAsync(int? companyId, int? itemVariantId, string value, string? typeCode, bool exact, CancellationToken ct);

    Task<IReadOnlyList<SerialItemDto>> ListAsync(int companyId, int? itemVariantId, int? warehouseId, byte? status, string? search, CancellationToken ct);

    Task<SerialDto?> GetAsync(int companyId, int serialId, CancellationToken ct);

    Task EditIdentifierAsync(int companyId, int serialId, string typeCode, string? value, string reason, int userId, CancellationToken ct);

    Task<IReadOnlyList<SerialIdentifierRuleDto>> SaveRuleAsync(int? itemId, int? itemGroupId, string typeCode, int sortOrder, bool requiredAtReceipt,
        bool requiredAtIssue, bool isUnique, bool onDocuments, bool delete, int userId, CancellationToken ct);

    Task<IReadOnlyList<(string Code, string? Mask, byte CheckKind)>> TypesAsync(CancellationToken ct);
}

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetSerials(int? ItemVariantId = null, int? WarehouseId = null, byte? Status = null, string? Search = null) : IQuery<IReadOnlyList<SerialItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetSerial(int Id) : IQuery<SerialDto>;

/// <summary>Corectarea unui identificator al bucatii, cu motiv si istoric (S4 3.4).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.SerialEditIdentifiers)]
[Audited("Inv.Serial.EditIdentifier")]
public sealed record EditSerialIdentifier(int SerialId, string TypeCode, string? Value, string Reason) : ICommand<bool>;

/// <summary>Regula unui identificator pe model sau grupa (S4 3.4).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.SettingsSerialIdentifiers)]
[Audited("Inv.SerialIdentifierRule.Save")]
public sealed record SaveSerialIdentifierRule(int? ItemId, int? ItemGroupId, string TypeCode, int SortOrder, bool RequiredAtReceipt, bool RequiredAtIssue,
    bool IsUnique, bool OnDocuments, bool Delete) : ICommand<IReadOnlyList<SerialIdentifierRuleDto>>;

internal sealed class EditSerialIdentifierValidator : AbstractValidator<EditSerialIdentifier>
{
    public EditSerialIdentifierValidator()
    {
        RuleFor(x => x.TypeCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Motivul este obligatoriu.").MaximumLength(400);
    }
}

internal sealed class SerialHandler(ISerialStore store, ICurrentContext context, IAuditScope audit)
    : IQueryHandler<GetSerials, IReadOnlyList<SerialItemDto>>, IQueryHandler<GetSerial, SerialDto>, ICommandHandler<EditSerialIdentifier, bool>,
    ICommandHandler<SaveSerialIdentifierRule, IReadOnlyList<SerialIdentifierRuleDto>>
{
    public Task<IReadOnlyList<SerialItemDto>> Handle(GetSerials request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.ItemVariantId, request.WarehouseId, request.Status,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(), cancellationToken);

    public async Task<SerialDto> Handle(GetSerial request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false)
        ?? throw ErpException.NotFound("INV_SERIAL_NOT_FOUND", "Seria nu există în firma curentă.");

    public async Task<bool> Handle(EditSerialIdentifier request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Value))
        {
            var type = (await store.TypesAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(t => string.Equals(t.Code, request.TypeCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (type.Code is not null && !SerialIdentifierRules.IsValid(request.Value, type.Mask, type.CheckKind))
            {
                throw new ErpException(ErpErrorKind.Validation, SerialIdentifierRules.Invalid, $"Identificatorul {request.TypeCode} nu are formatul cerut.");
            }
        }

        await store.EditIdentifierAsync(context.CompanyId, request.SerialId, request.TypeCode.Trim(), request.Value, request.Reason.Trim(), context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Serial", request.SerialId, new { request.TypeCode, request.Value, request.Reason });
        return true;
    }

    public async Task<IReadOnlyList<SerialIdentifierRuleDto>> Handle(SaveSerialIdentifierRule request, CancellationToken cancellationToken)
    {
        var rules = await store.SaveRuleAsync(request.ItemId, request.ItemGroupId, request.TypeCode.Trim(), request.SortOrder, request.RequiredAtReceipt,
            request.RequiredAtIssue, request.IsUnique, request.OnDocuments, request.Delete, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.SerialIdentifierRule", request.ItemId ?? request.ItemGroupId ?? 0, request);
        return rules;
    }
}
