using Administration.Domain.Partners;
using FluentValidation;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.PartnerTypes.Upsert;

public sealed record UpsertPartnerTypeCommand(
    byte? PartnerTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    bool AffectsIssuedInvoices,
    bool AffectsReceivedInvoices,
    short SortOrder
) : ICommand<byte>;

public sealed class UpsertPartnerTypeCommandValidator : AbstractValidator<UpsertPartnerTypeCommand>
{
    public UpsertPartnerTypeCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(@"^[A-Z0-9_]+$")
            .WithMessage("Codul trebuie să conțină doar litere mari, cifre și underscore.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);
    }
}

public sealed class UpsertPartnerTypeCommandHandler(
    IPartnerTypeRepository repository,
    ICurrentUser currentUser)
    : ICommandHandler<UpsertPartnerTypeCommand, byte>
{
    public async Task<Result<byte>> Handle(
        UpsertPartnerTypeCommand command,
        CancellationToken cancellationToken)
    {
        var data = new PartnerTypeUpsertData(
            command.PartnerTypeId,
            command.Code.ToUpperInvariant(),
            command.Name,
            command.Description,
            command.IsActive,
            command.AffectsIssuedInvoices,
            command.AffectsReceivedInvoices,
            command.SortOrder,
            currentUser.Email
        );

        byte newId = await repository.UpsertAsync(data, cancellationToken);
        return Result.Success(newId);
    }
}
