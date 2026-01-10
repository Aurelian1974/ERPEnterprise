using FluentValidation;
using ValyanERP.Web.Features.Administrare.Persoane.Models;

namespace ValyanERP.Web.Features.Administrare.Persoane.Validators;

public class PersoanaValidator : AbstractValidator<Persoana>
{
    public PersoanaValidator()
    {
        RuleFor(x => x.Nume).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Prenume).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CNP).Length(13).When(x => !string.IsNullOrEmpty(x.CNP));
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}
