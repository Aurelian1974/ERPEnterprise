using System.Globalization;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Partners;

/// <summary>Masca blocarii partenerului: 1 vanzare, 2 achizitie, 4 plati (7 = tot); a articolului: 1 vanzare, 2 achizitie.</summary>
public static class BlockFlags
{
    public const byte Sales = 1;
    public const byte Purchases = 2;
    public const byte Payments = 4;
    public const byte PartnerAll = 7;
    public const byte ItemAll = 3;
}

/// <summary>
/// Tab-ul „Blocare și credit” al partenerului: blocarea (comuna firmelor) si limita de credit a firmei curente.
/// OverLimitAction: 1 avertizare, 2 blocare. RowVersion-urile sunt null pana la prima salvare.
/// </summary>
public sealed record PartnerBlockDto(byte BlockedFor, string? Reason, DateTime? BlockedAt, string? BlockedByName, byte[]? BlockRowVersion,
    string? CreditLimit, byte OverLimitAction, byte[]? CreditRowVersion);

public sealed record ItemBlockDto(byte BlockedFor, string? Reason, DateTime? BlockedAt, string? BlockedByName, byte[]? RowVersion);

/// <summary>Randul folosit de verificarea de pe documente (core.usp_MasterDataBlock_Check).</summary>
public sealed record BlockState(string Name, bool IsActive, byte BlockedFor, string? Reason, decimal? CreditLimit, byte OverLimitAction);

/// <summary>Accesul la date pentru blocari si limita de credit (NET-06).</summary>
public interface IMasterDataBlockStore
{
    Task<PartnerBlockDto?> GetPartnerAsync(int partnerId, int companyId, CancellationToken ct);

    Task<ItemBlockDto?> GetItemAsync(int itemId, CancellationToken ct);

    Task<SavedEntity> SavePartnerBlockAsync(int partnerId, byte blockedFor, string? reason, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<SavedEntity> SaveItemBlockAsync(int itemId, byte blockedFor, string? reason, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<SavedEntity> SaveCreditAsync(int partnerId, int companyId, decimal? creditLimit, byte overLimitAction, byte[]? rowVersion, int userId,
        CancellationToken ct);

    Task<BlockState?> GetStateAsync(int companyId, int? partnerId, int? itemId, CancellationToken ct);
}

[Module("Core")]
[RequiresPermission(CorePermissions.PartnerView)]
public sealed record GetPartnerBlock(int PartnerId) : IQuery<PartnerBlockDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record GetItemBlock(int ItemId) : IQuery<ItemBlockDto>;

/// <summary>Blocarea partiala a partenerului, cu motiv obligatoriu; 0 = deblocare.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
[Audited("Core.Partner.Block")]
public sealed record SetPartnerBlock(int PartnerId, byte BlockedFor, string? Reason, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>Blocarea partiala a articolului (vanzare, achizitie), cu motiv obligatoriu; 0 = deblocare.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.Item.Block")]
public sealed record SetItemBlock(int ItemId, byte BlockedFor, string? Reason, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>Limita de credit a clientului pe firma curenta; CreditLimit = sir zecimal invariant (UI-05) sau null = fara limita.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManageCredit)]
[Audited("Core.Partner.CreditLimit")]
public sealed record SetPartnerCreditLimit(int PartnerId, string? CreditLimit, byte OverLimitAction, byte[]? RowVersion) : ICommand<SavedEntity>;

internal sealed class SetPartnerBlockValidator : AbstractValidator<SetPartnerBlock>
{
    public SetPartnerBlockValidator()
    {
        RuleFor(x => x.BlockedFor).LessThanOrEqualTo(BlockFlags.PartnerAll).WithMessage("Alegeți pentru ce se blochează partenerul.");
        RuleFor(x => x.Reason).NotEmpty().When(x => x.BlockedFor > 0).WithMessage("Motivul blocării este obligatoriu.")
            .MaximumLength(300).WithMessage("Motivul are cel mult 300 de caractere.");
    }
}

internal sealed class SetItemBlockValidator : AbstractValidator<SetItemBlock>
{
    public SetItemBlockValidator()
    {
        RuleFor(x => x.BlockedFor).LessThanOrEqualTo(BlockFlags.ItemAll).WithMessage("Alegeți pentru ce se blochează articolul.");
        RuleFor(x => x.Reason).NotEmpty().When(x => x.BlockedFor > 0).WithMessage("Motivul blocării este obligatoriu.")
            .MaximumLength(300).WithMessage("Motivul are cel mult 300 de caractere.");
    }
}

internal sealed class SetPartnerCreditLimitValidator : AbstractValidator<SetPartnerCreditLimit>
{
    public SetPartnerCreditLimitValidator()
    {
        RuleFor(x => x.CreditLimit).Must(v => v is null || (decimal.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
                && d >= 0 && decimal.Round(d, 2) == d && d < 100_000_000_000m))
            .WithMessage("Limita de credit este o sumă pozitivă, cu cel mult 2 zecimale.");
        RuleFor(x => x.OverLimitAction).InclusiveBetween((byte)1, (byte)2).WithMessage("Alegeți ce se întâmplă la depășire.");
    }
}

internal sealed class GetPartnerBlockHandler(IMasterDataBlockStore store, IOwnerAccess owners, ICurrentContext context)
    : IQueryHandler<GetPartnerBlock, PartnerBlockDto>
{
    public async Task<PartnerBlockDto> Handle(GetPartnerBlock request, CancellationToken cancellationToken)
    {
        await owners.EnsureVisibleAsync(OwnedEntities.Partner, request.PartnerId, CorePermissions.PartnerView, cancellationToken).ConfigureAwait(false);
        return await store.GetPartnerAsync(request.PartnerId, context.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Partenerul nu există.");
    }
}

internal sealed class GetItemBlockHandler(IMasterDataBlockStore store, IOwnerAccess owners) : IQueryHandler<GetItemBlock, ItemBlockDto>
{
    public async Task<ItemBlockDto> Handle(GetItemBlock request, CancellationToken cancellationToken)
    {
        await owners.EnsureVisibleAsync(OwnedEntities.Item, request.ItemId, CorePermissions.ItemView, cancellationToken).ConfigureAwait(false);
        return await store.GetItemAsync(request.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Articolul nu există.");
    }
}

internal sealed class SetPartnerBlockHandler(IMasterDataBlockStore store, IOwnerAccess owners, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetPartnerBlock, SavedEntity>
{
    public async Task<SavedEntity> Handle(SetPartnerBlock request, CancellationToken cancellationToken)
    {
        await owners.EnsureManageAsync(OwnedEntities.Partner, request.PartnerId, null, CorePermissions.PartnerManage, cancellationToken).ConfigureAwait(false);
        var reason = request.BlockedFor == 0 ? null : request.Reason?.Trim();
        var saved = await store.SavePartnerBlockAsync(request.PartnerId, request.BlockedFor, reason, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Core.Partner", request.PartnerId, new { request.BlockedFor, BlockReason = reason });
        return saved;
    }
}

internal sealed class SetItemBlockHandler(IMasterDataBlockStore store, IOwnerAccess owners, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetItemBlock, SavedEntity>
{
    public async Task<SavedEntity> Handle(SetItemBlock request, CancellationToken cancellationToken)
    {
        await owners.EnsureManageAsync(OwnedEntities.Item, request.ItemId, null, CorePermissions.ItemManage, cancellationToken).ConfigureAwait(false);
        var reason = request.BlockedFor == 0 ? null : request.Reason?.Trim();
        var saved = await store.SaveItemBlockAsync(request.ItemId, request.BlockedFor, reason, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Core.Item", request.ItemId, new { request.BlockedFor, BlockReason = reason });
        return saved;
    }
}

internal sealed class SetPartnerCreditLimitHandler(IMasterDataBlockStore store, IOwnerAccess owners, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetPartnerCreditLimit, SavedEntity>
{
    public async Task<SavedEntity> Handle(SetPartnerCreditLimit request, CancellationToken cancellationToken)
    {
        if (context.CompanyId == 0)
        {
            throw new ErpException(ErpErrorKind.Validation, "CORE_COMPANY_REQUIRED", "Limita de credit se stabilește pe o firmă: alegeți firma din antet.");
        }

        await owners.EnsureVisibleAsync(OwnedEntities.Partner, request.PartnerId, CorePermissions.PartnerView, cancellationToken).ConfigureAwait(false);
        decimal? limit = request.CreditLimit is null ? null : decimal.Parse(request.CreditLimit, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var saved = await store.SaveCreditAsync(request.PartnerId, context.CompanyId, limit, request.OverLimitAction, request.RowVersion, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Partner", request.PartnerId, new { context.CompanyId, CreditLimit = limit, request.OverLimitAction });
        return saved;
    }
}

/// <summary>
/// IMasterDataBlockCheck (02.01, 6): inactiv sau blocat pentru scopul cerut = Blocked; limita de credit depasita (expunerea din
/// IPartnerExposureProvider plus documentul curent) = Warning sau Blocked, dupa setarea clientului pe firma. Doar la vanzare.
/// </summary>
public sealed class MasterDataBlockCheck(IMasterDataBlockStore store, IPartnerExposureProvider exposure) : IMasterDataBlockCheck
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public async Task<BlockCheckResult> CheckPartnerAsync(int companyId, int partnerId, BlockPurpose purpose, decimal additionalAmount, CancellationToken ct)
    {
        var state = await store.GetStateAsync(companyId, partnerId, null, ct).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Partenerul nu există.");
        if (!state.IsActive)
        {
            return new BlockCheckResult(BlockLevel.Blocked, "CORE_PARTNER_INACTIVE", $"Partenerul {state.Name} este inactiv.");
        }

        if ((state.BlockedFor & (byte)purpose) != 0)
        {
            return new BlockCheckResult(BlockLevel.Blocked, "CORE_PARTNER_BLOCKED",
                $"Partenerul {state.Name} este blocat pentru {PurposeText(purpose)}: {state.Reason}");
        }

        if (purpose != BlockPurpose.Sales || state.CreditLimit is not { } limit)
        {
            return BlockCheckResult.Ok;
        }

        var total = await exposure.GetExposureAsync(companyId, partnerId, ct).ConfigureAwait(false) + additionalAmount;
        if (total <= limit)
        {
            return BlockCheckResult.Ok;
        }

        var message = $"Clientul {state.Name} depășește limita de credit: {total.ToString("N2", Ro)} lei față de {limit.ToString("N2", Ro)} lei.";
        return new BlockCheckResult(state.OverLimitAction == 2 ? BlockLevel.Blocked : BlockLevel.Warning, "CORE_CREDIT_LIMIT_EXCEEDED", message);
    }

    public async Task<BlockCheckResult> CheckItemAsync(int itemId, BlockPurpose purpose, CancellationToken ct)
    {
        var state = await store.GetStateAsync(0, null, itemId, ct).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Articolul nu există.");
        if (!state.IsActive)
        {
            return new BlockCheckResult(BlockLevel.Blocked, "CORE_ITEM_INACTIVE", $"Articolul {state.Name} este inactiv.");
        }

        return (state.BlockedFor & (byte)purpose) != 0
            ? new BlockCheckResult(BlockLevel.Blocked, "CORE_ITEM_BLOCKED", $"Articolul {state.Name} este blocat pentru {PurposeText(purpose)}: {state.Reason}")
            : BlockCheckResult.Ok;
    }

    private static string PurposeText(BlockPurpose purpose) => purpose switch
    {
        BlockPurpose.Sales => "vânzare",
        BlockPurpose.Purchases => "achiziție",
        _ => "plăți",
    };
}

/// <summary>Pana la modulul care tine soldurile clientilor, expunerea e 0 (limita se compara doar cu documentul curent).</summary>
public sealed class NoPartnerExposure : IPartnerExposureProvider
{
    public Task<decimal> GetExposureAsync(int companyId, int partnerId, CancellationToken ct) => Task.FromResult(0m);
}
