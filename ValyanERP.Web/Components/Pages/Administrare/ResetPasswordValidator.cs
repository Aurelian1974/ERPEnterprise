using FluentValidation;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Model for password reset form validation.
/// </summary>
public class ResetPasswordModel
{
    /// <summary>
    /// The new password.
    /// </summary>
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>
    /// Password confirmation.
    /// </summary>
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>
/// FluentValidation validator for password reset.
/// Reads validation rules from system parameters.
/// </summary>
public class ResetPasswordValidator : AbstractValidator<ResetPasswordModel>
{
    public ResetPasswordValidator(int minPasswordLength = 8, bool requireSpecialChar = true)
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Parola este obligatorie")
            .MinimumLength(minPasswordLength).WithMessage($"Parola trebuie să aibă cel puțin {minPasswordLength} caractere")
            .Matches("[A-Z]").WithMessage("Parola trebuie să conțină cel puțin o literă mare")
            .Matches("[a-z]").WithMessage("Parola trebuie să conțină cel puțin o literă mică")
            .Matches("[0-9]").WithMessage("Parola trebuie să conțină cel puțin o cifră");

        if (requireSpecialChar)
        {
            RuleFor(x => x.NewPassword)
                .Matches("[!@#$%^&*()_+-=[]{}|;:,.<>?]").WithMessage("Parola trebuie să conțină cel puțin un caracter special (!@#$%^&* etc.)");
        }

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirmarea parolei este obligatorie")
            .Equal(x => x.NewPassword).WithMessage("Parolele nu se potrivesc");
    }
}