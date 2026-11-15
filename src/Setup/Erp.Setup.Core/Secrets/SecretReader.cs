namespace Erp.Setup.Core.Secrets;

/// <summary>Citirea parolei inainte de a fi folosita; nu se scrie nicaieri (R6).</summary>
public interface ISecretInput
{
    /// <summary>Citeste o valoare cu ecou ascuns; null daca operatorul renunta (Esc sau sfarsitul intrarii).</summary>
    string? ReadHidden(string prompt);

    void WriteLine(string message);
}

public enum SecretOutcome
{
    Provided,
    Generate,
    Missing,
    Rejected,
    Cancelled,
}

public sealed record SecretResult(SecretOutcome Outcome, string? Value = null, bool FromEnvironment = false, string? Message = null)
{
    /// <summary>Codul de iesire la esec: 11 pentru lipsa/respingere (--unattended), 2 pentru anulare.</summary>
    public int ExitCode => Outcome switch
    {
        SecretOutcome.Cancelled => ExitCodes.CancelledByOperator,
        SecretOutcome.Missing or SecretOutcome.Rejected => ExitCodes.InvalidAnswersFile,
        _ => ExitCodes.Success,
    };
}

/// <summary>Parolele cerute interactiv sau, cu --unattended, citite din variabilele de mediu (6.3).</summary>
public sealed class SecretReader(Func<string, string?> getEnvironmentVariable, ISecretInput? input)
{
    public const string OperatorPasswordVariable = "ERP_SQL_OPERATOR_PASSWORD";
    public const string AdminPasswordVariable = "ERP_ADMIN_INITIAL_PASSWORD";

    public SecretResult ReadOperatorSqlPassword(bool unattended)
    {
        var fromEnvironment = getEnvironmentVariable(OperatorPasswordVariable);
        if (unattended)
        {
            return string.IsNullOrEmpty(fromEnvironment)
                ? new SecretResult(SecretOutcome.Missing,
                    Message: $"Variabila de mediu {OperatorPasswordVariable} lipsește (necesară pentru operatorAuthentication = Sql).")
                : new SecretResult(SecretOutcome.Provided, fromEnvironment, FromEnvironment: true);
        }

        while (true)
        {
            var value = RequireInput().ReadHidden("Parola SQL a operatorului: ");
            if (value is null)
            {
                return new SecretResult(SecretOutcome.Cancelled, Message: "Anulat de operator.");
            }

            if (value.Length > 0)
            {
                return new SecretResult(SecretOutcome.Provided, value);
            }
        }
    }

    /// <summary>
    /// Parola initiala a administratorului (8.8): din variabila de mediu sau introdusa cu confirmare;
    /// altfel <see cref="SecretOutcome.Generate"/> (parola se genereaza la pasul I-13).
    /// </summary>
    public SecretResult ReadInitialAdminPassword(bool unattended, string userName)
    {
        var fromEnvironment = getEnvironmentVariable(AdminPasswordVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            var problems = InitialPasswordPolicy.Validate(fromEnvironment, userName);
            if (problems.Count == 0)
            {
                return new SecretResult(SecretOutcome.Provided, fromEnvironment, FromEnvironment: true);
            }

            var message = $"Parola din {AdminPasswordVariable} nu respectă politica: {string.Join(" ", problems)}";
            if (unattended)
            {
                return new SecretResult(SecretOutcome.Rejected, Message: message);
            }

            RequireInput().WriteLine(message);
        }
        else if (unattended)
        {
            return new SecretResult(SecretOutcome.Generate);
        }

        var console = RequireInput();
        while (true)
        {
            var value = console.ReadHidden("Parola inițială a administratorului (Enter = generată automat): ");
            if (value is null)
            {
                return new SecretResult(SecretOutcome.Cancelled, Message: "Anulat de operator.");
            }

            if (value.Length == 0)
            {
                return new SecretResult(SecretOutcome.Generate);
            }

            var problems = InitialPasswordPolicy.Validate(value, userName);
            if (problems.Count > 0)
            {
                console.WriteLine(string.Join(" ", problems));
                continue;
            }

            var confirmation = console.ReadHidden("Confirmați parola: ");
            if (confirmation is null)
            {
                return new SecretResult(SecretOutcome.Cancelled, Message: "Anulat de operator.");
            }

            if (string.Equals(value, confirmation, StringComparison.Ordinal))
            {
                return new SecretResult(SecretOutcome.Provided, value);
            }

            console.WriteLine("Parolele nu coincid.");
        }
    }

    private ISecretInput RequireInput() =>
        input ?? throw new InvalidOperationException("Citirea interactivă nu este disponibilă.");
}

/// <summary>Politica parolei initiale a administratorului (8.8, punctul 1).</summary>
public static class InitialPasswordPolicy
{
    public const int MinimumLength = 12;

    public static IReadOnlyList<string> Validate(string password, string userName)
    {
        var problems = new List<string>();
        if (password.Length < MinimumLength)
        {
            problems.Add($"Minimum {MinimumLength} caractere.");
        }

        if (!password.Any(char.IsUpper))
        {
            problems.Add("Cel puțin o literă mare.");
        }

        if (!password.Any(char.IsLower))
        {
            problems.Add("Cel puțin o literă mică.");
        }

        if (!password.Any(char.IsDigit))
        {
            problems.Add("Cel puțin o cifră.");
        }

        if (password.All(char.IsLetterOrDigit))
        {
            problems.Add("Cel puțin un simbol.");
        }

        if (userName.Length > 0 && password.Contains(userName, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Nu poate conține numele de utilizator.");
        }

        return problems;
    }
}
