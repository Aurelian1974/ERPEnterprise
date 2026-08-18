using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.SubEntities;

// ─── Addresses ─────────────────────────────────────────────────────────────────

public sealed record UpsertPartnerAddressCommand(
    Guid PartnerId,
    long? Id,
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
) : ICommand;

public sealed class UpsertPartnerAddressCommandHandler(
    IPartnerSubEntityRepository subRepo,
    IPartnerReadRepository readRepo,
    ITenantContext tenant)
    : ICommandHandler<UpsertPartnerAddressCommand>
{
    public async Task<Result> Handle(
        UpsertPartnerAddressCommand command,
        CancellationToken cancellationToken)
    {
        var exists = await readRepo.GetByIdAsync(command.PartnerId, tenant.TenantId, cancellationToken);
        if (exists is null)
            return Result.Failure(AdministrationErrors.PartnerNotFound(command.PartnerId));

        await subRepo.UpsertAddressAsync(
            command.Id, command.PartnerId, tenant.TenantId,
            command.AddressType, command.Street,
            command.StreetNumber, command.Block, command.Staircase,
            command.Floor, command.Apartment, command.Building,
            command.City, command.County, command.PostalCode, command.Country,
            command.IsPrimary, cancellationToken);

        return Result.Success();
    }
}

public sealed record DeletePartnerAddressCommand(
    Guid PartnerId,
    long Id
) : ICommand;

public sealed class DeletePartnerAddressCommandHandler(
    IPartnerSubEntityRepository subRepo,
    ITenantContext tenant)
    : ICommandHandler<DeletePartnerAddressCommand>
{
    public async Task<Result> Handle(
        DeletePartnerAddressCommand command,
        CancellationToken cancellationToken)
    {
        await subRepo.DeleteAddressAsync(command.Id, command.PartnerId, tenant.TenantId, cancellationToken);
        return Result.Success();
    }
}

// ─── Contacts ──────────────────────────────────────────────────────────────────

public sealed record UpsertPartnerContactCommand(
    Guid PartnerId,
    long? Id,
    string FullName,
    string? Position,
    string? Phone,
    string? Email,
    bool IsPrimary
) : ICommand;

public sealed class UpsertPartnerContactCommandHandler(
    IPartnerSubEntityRepository subRepo,
    IPartnerReadRepository readRepo,
    ITenantContext tenant)
    : ICommandHandler<UpsertPartnerContactCommand>
{
    public async Task<Result> Handle(
        UpsertPartnerContactCommand command,
        CancellationToken cancellationToken)
    {
        var exists = await readRepo.GetByIdAsync(command.PartnerId, tenant.TenantId, cancellationToken);
        if (exists is null)
            return Result.Failure(AdministrationErrors.PartnerNotFound(command.PartnerId));

        await subRepo.UpsertContactAsync(
            command.Id, command.PartnerId, tenant.TenantId,
            command.FullName, command.Position,
            command.Phone, command.Email,
            command.IsPrimary, cancellationToken);

        return Result.Success();
    }
}

public sealed record DeletePartnerContactCommand(
    Guid PartnerId,
    long Id
) : ICommand;

public sealed class DeletePartnerContactCommandHandler(
    IPartnerSubEntityRepository subRepo,
    ITenantContext tenant)
    : ICommandHandler<DeletePartnerContactCommand>
{
    public async Task<Result> Handle(
        DeletePartnerContactCommand command,
        CancellationToken cancellationToken)
    {
        await subRepo.DeleteContactAsync(command.Id, command.PartnerId, tenant.TenantId, cancellationToken);
        return Result.Success();
    }
}

// ─── Bank Accounts ─────────────────────────────────────────────────────────────

public sealed record UpsertPartnerBankAccountCommand(
    Guid PartnerId,
    long? Id,
    string Iban,
    string BankName,
    string Currency,
    bool IsDefault
) : ICommand;

public sealed class UpsertPartnerBankAccountCommandHandler(
    IPartnerSubEntityRepository subRepo,
    IPartnerReadRepository readRepo,
    ITenantContext tenant)
    : ICommandHandler<UpsertPartnerBankAccountCommand>
{
    public async Task<Result> Handle(
        UpsertPartnerBankAccountCommand command,
        CancellationToken cancellationToken)
    {
        var exists = await readRepo.GetByIdAsync(command.PartnerId, tenant.TenantId, cancellationToken);
        if (exists is null)
            return Result.Failure(AdministrationErrors.PartnerNotFound(command.PartnerId));

        await subRepo.UpsertBankAccountAsync(
            command.Id, command.PartnerId, tenant.TenantId,
            command.Iban, command.BankName,
            command.Currency, command.IsDefault,
            cancellationToken);

        return Result.Success();
    }
}

public sealed record DeletePartnerBankAccountCommand(
    Guid PartnerId,
    long Id
) : ICommand;

public sealed class DeletePartnerBankAccountCommandHandler(
    IPartnerSubEntityRepository subRepo,
    ITenantContext tenant)
    : ICommandHandler<DeletePartnerBankAccountCommand>
{
    public async Task<Result> Handle(
        DeletePartnerBankAccountCommand command,
        CancellationToken cancellationToken)
    {
        await subRepo.DeleteBankAccountAsync(command.Id, command.PartnerId, tenant.TenantId, cancellationToken);
        return Result.Success();
    }
}
