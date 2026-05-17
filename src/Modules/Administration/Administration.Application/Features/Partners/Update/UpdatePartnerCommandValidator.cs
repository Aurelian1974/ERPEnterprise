using FluentValidation;

namespace Administration.Application.Features.Partners.Update;

public sealed class UpdatePartnerCommandValidator : AbstractValidator<UpdatePartnerCommand>
{
    public UpdatePartnerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("ID-ul partenerului este obligatoriu.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Codul este obligatoriu.")
            .MaximumLength(20).WithMessage("Codul poate avea maxim 20 de caractere.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Denumirea este obligatorie.")
            .MaximumLength(200).WithMessage("Denumirea poate avea maxim 200 de caractere.");

        RuleFor(x => x.Cui)
            .MaximumLength(20).WithMessage("CUI-ul poate avea maxim 20 de caractere.")
            .When(x => x.Cui is not null);

        RuleFor(x => x.RegistrationNumber)
            .MaximumLength(50).WithMessage("Numărul de registru poate avea maxim 50 de caractere.")
            .When(x => x.RegistrationNumber is not null);

        RuleFor(x => x.LegalForm)
            .MaximumLength(50).WithMessage("Forma juridică poate avea maxim 50 de caractere.")
            .When(x => x.LegalForm is not null);

        RuleFor(x => x.Phone)
            .MaximumLength(30).WithMessage("Telefonul poate avea maxim 30 de caractere.")
            .When(x => x.Phone is not null);

        RuleFor(x => x.Email)
            .MaximumLength(200).WithMessage("Email-ul poate avea maxim 200 de caractere.")
            .EmailAddress().WithMessage("Adresa de email nu este validă.")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notele pot avea maxim 1000 de caractere.")
            .When(x => x.Notes is not null);
    }
}
