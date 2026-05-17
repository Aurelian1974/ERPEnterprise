using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.Update;

public sealed record UpdatePartnerCommand(
    Guid Id,
    string Code,
    string Name,
    string? Cui,
    string? RegistrationNumber,
    string? LegalForm,
    byte? PartnerTypeId,
    bool IsVatPayer,
    string? Phone,
    string? Email,
    bool IsActive,
    string? Notes
) : ICommand;

public sealed class UpdatePartnerCommandHandler(
    IPartnerRepository repository,
    IPartnerReadRepository readRepo,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : ICommandHandler<UpdatePartnerCommand>
{
    public async Task<Result> Handle(
        UpdatePartnerCommand command,
        CancellationToken cancellationToken)
    {
        var partner = await repository.GetByIdAsync(command.Id, tenant.TenantId, cancellationToken);

        if (partner is null)
            return Result.Failure(AdministrationErrors.PartnerNotFound(command.Id));

        var codeExists = await readRepo.CodeExistsAsync(
            tenant.TenantId, command.Code, excludeId: command.Id, cancellationToken);

        if (codeExists)
            return Result.Failure(AdministrationErrors.PartnerCodeAlreadyExists(command.Code));

        partner.Update(
            code:               command.Code,
            name:               command.Name,
            updatedBy:          currentUser.UserId,
            cui:                command.Cui,
            registrationNumber: command.RegistrationNumber,
            legalForm:          command.LegalForm,
            partnerTypeId:      command.PartnerTypeId,
            isVatPayer:         command.IsVatPayer,
            phone:              command.Phone,
            email:              command.Email,
            isActive:           command.IsActive,
            notes:              command.Notes);

        await repository.UpdateAsync(partner, cancellationToken);

        return Result.Success();
    }
}
