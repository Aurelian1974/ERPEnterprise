using Administration.Domain.Partners;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.PartnerTypes.Delete;

public sealed record DeletePartnerTypeCommand(byte PartnerTypeId) : ICommand;

public sealed class DeletePartnerTypeCommandHandler(IPartnerTypeRepository repository)
    : ICommandHandler<DeletePartnerTypeCommand>
{
    public async Task<Result> Handle(
        DeletePartnerTypeCommand command,
        CancellationToken cancellationToken)
    {
        await repository.DeleteAsync(command.PartnerTypeId, cancellationToken);
        return Result.Success();
    }
}
