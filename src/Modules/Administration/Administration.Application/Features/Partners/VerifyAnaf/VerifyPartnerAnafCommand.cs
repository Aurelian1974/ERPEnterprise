using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.VerifyAnaf;

public sealed record VerifyPartnerAnafCommand(Guid PartnerId) : ICommand;

public sealed class VerifyPartnerAnafCommandHandler(
    IPartnerRepository repository,
    IAnafService anafService,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : ICommandHandler<VerifyPartnerAnafCommand>
{
    public async Task<Result> Handle(
        VerifyPartnerAnafCommand command,
        CancellationToken cancellationToken)
    {
        var partner = await repository.GetByIdAsync(command.PartnerId, tenant.TenantId, cancellationToken);

        if (partner is null)
            return Result.Failure(AdministrationErrors.PartnerNotFound(command.PartnerId));

        if (string.IsNullOrWhiteSpace(partner.Cui))
            return Result.Failure(AdministrationErrors.PartnerCuiMissing(command.PartnerId));

        var result = await anafService.VerifyAsync(partner.Cui, cancellationToken);

        if (!result.IsSuccess)
            return Result.Failure(AdministrationErrors.AnafVerificationFailed(result.ErrorMessage!));

        partner.ApplyAnafData(
            result.Data!.ScpTva,
            result.Data.NrRegCom,
            result.Data.FormaJuridica,
            result.Data.Telefon,
            currentUser.UserId);

        await repository.UpdateAnafAsync(partner, result.Data.AdresaSediuSocial, cancellationToken);

        return Result.Success();
    }
}
