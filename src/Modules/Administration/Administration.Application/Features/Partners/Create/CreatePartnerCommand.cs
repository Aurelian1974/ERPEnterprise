using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Administration.Domain.Partners;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.Create;

public sealed record CreatePartnerCommand(
    string Code,
    string Name,
    string? Cui,
    string? RegistrationNumber,
    string? LegalForm,
    byte? PartnerTypeId,
    bool IsVatPayer,
    string? Phone,
    string? Email,
    string? Notes,
    DateTime? AnafVerifiedAt
) : ICommand<Guid>;

public sealed class CreatePartnerCommandHandler(
    IPartnerRepository repository,
    IPartnerReadRepository readRepo,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : ICommandHandler<CreatePartnerCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreatePartnerCommand command,
        CancellationToken cancellationToken)
    {
        var codeExists = await readRepo.CodeExistsAsync(
            tenant.TenantId, command.Code, excludeId: null, cancellationToken);

        if (codeExists)
            return Result.Failure<Guid>(AdministrationErrors.PartnerCodeAlreadyExists(command.Code));

        var partner = Partner.Create(
            tenantId:           tenant.TenantId,
            code:               command.Code,
            name:               command.Name,
            createdBy:          currentUser.UserId,
            cui:                command.Cui,
            registrationNumber: command.RegistrationNumber,
            legalForm:          command.LegalForm,
            partnerTypeId:      command.PartnerTypeId,
            isVatPayer:         command.IsVatPayer,
            phone:              command.Phone,
            email:              command.Email,
            notes:              command.Notes,
            anafVerifiedAt:     command.AnafVerifiedAt);

        await repository.InsertAsync(partner, cancellationToken);

        return Result.Success(partner.Id);
    }
}
