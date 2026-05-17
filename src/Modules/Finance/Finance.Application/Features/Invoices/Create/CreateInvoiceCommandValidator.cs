using FluentValidation;

namespace Finance.Application.Features.Invoices.Create;

public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Z]{3}$").WithMessage("Currency must be a 3-letter ISO code (e.g. RON, EUR).");
        RuleFor(x => x.DueDate).GreaterThan(DateOnly.FromDateTime(DateTime.Today));
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one invoice line is required.");
        RuleForEach(x => x.Lines).SetValidator(new CreateInvoiceLineCommandValidator());
    }
}

public sealed class CreateInvoiceLineCommandValidator : AbstractValidator<CreateInvoiceLineCommand>
{
    public CreateInvoiceLineCommandValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.VatRate).InclusiveBetween(0, 100);
    }
}
