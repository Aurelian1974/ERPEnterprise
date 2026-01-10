using System.Text.RegularExpressions;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Services;

/// <summary>
/// Service for masking sensitive data in audit logs.
/// Implements GDPR-compliant data masking for passwords, CNP, emails, and other sensitive fields.
/// </summary>
public class SensitiveDataMasker
{
    // Sensitive field name patterns (case-insensitive)
    private static readonly string[] SensitiveFieldNames =
    {
        "password", "passwordhash", "confirmpassword", "newpassword", "oldpassword",
        "cnp", "ssn", "taxid", "socialsecurity",
        "creditcard", "cardnumber", "cvv", "cvc",
        "token", "apikey", "secret", "privatekey",
        "pin", "otp", "code"
    };

    /// <summary>
    /// Determines if a field name represents sensitive data.
    /// </summary>
    /// <param name="fieldName">The field name to check.</param>
    /// <returns>True if the field is sensitive, false otherwise.</returns>
    public bool IsSensitiveField(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return false;

        var lowerFieldName = fieldName.ToLowerInvariant();
        return SensitiveFieldNames.Any(pattern => lowerFieldName.Contains(pattern));
    }

    /// <summary>
    /// Masks a value based on the field name and content.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    /// <param name="value">The value to mask.</param>
    /// <returns>The masked value.</returns>
    public string? MaskValue(string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (!IsSensitiveField(fieldName))
            return value;

        var lowerFieldName = fieldName.ToLowerInvariant();

        // Password fields: always mask completely
        if (lowerFieldName.Contains("password"))
            return MaskPassword(value);

        // CNP/SSN: show last 4 digits
        if (lowerFieldName.Contains("cnp") || lowerFieldName.Contains("ssn") || lowerFieldName.Contains("taxid"))
            return MaskCNP(value);

        // Email: partial masking
        if (lowerFieldName.Contains("email") && value.Contains('@'))
            return MaskEmail(value);

        // Credit card: show last 4 digits
        if (lowerFieldName.Contains("card") || lowerFieldName.Contains("cvv"))
            return MaskCreditCard(value);

        // Default: mask completely
        return "*****";
    }

    /// <summary>
    /// Masks a password (always returns "*****").
    /// </summary>
    /// <param name="value">The password value.</param>
    /// <returns>Masked password.</returns>
    public string MaskPassword(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : "*****";
    }

    /// <summary>
    /// Masks a CNP/SSN, showing only the last 4 digits.
    /// Example: "1234567890123" -> "******7890123" (last 7) or "******0123" (last 4)
    /// </summary>
    /// <param name="cnp">The CNP/SSN value.</param>
    /// <returns>Masked CNP.</returns>
    public string MaskCNP(string? cnp)
    {
        if (string.IsNullOrWhiteSpace(cnp))
            return string.Empty;

        // Romanian CNP is 13 digits - show last 4
        if (cnp.Length >= 4)
        {
            var visibleDigits = 4;
            var maskedPart = new string('*', cnp.Length - visibleDigits);
            var visiblePart = cnp.Substring(cnp.Length - visibleDigits);
            return maskedPart + visiblePart;
        }

        // Too short - mask everything
        return new string('*', cnp.Length);
    }

    /// <summary>
    /// Masks an email address partially.
    /// Example: "john.doe@example.com" -> "j***@example.com"
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <returns>Masked email.</returns>
    public string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return email ?? string.Empty;

        var parts = email.Split('@');
        if (parts.Length != 2)
            return email;

        var localPart = parts[0];
        var domain = parts[1];

        if (localPart.Length <= 1)
            return $"{localPart}@{domain}";

        // Show first character + *** + @ + domain
        var masked = $"{localPart[0]}***@{domain}";
        return masked;
    }

    /// <summary>
    /// Masks a credit card number, showing only the last 4 digits.
    /// Example: "1234567890123456" -> "****-****-****-3456"
    /// </summary>
    /// <param name="cardNumber">The credit card number.</param>
    /// <returns>Masked card number.</returns>
    public string MaskCreditCard(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            return string.Empty;

        // Remove spaces and dashes
        var cleaned = Regex.Replace(cardNumber, @"[\s\-]", "");

        if (cleaned.Length >= 4)
        {
            var last4 = cleaned.Substring(cleaned.Length - 4);
            return $"****-****-****-{last4}";
        }

        // Too short - mask everything
        return "****";
    }

    /// <summary>
    /// Masks an entire object's sensitive properties (used for logging).
    /// </summary>
    /// <typeparam name="T">Type of the object.</typeparam>
    /// <param name="obj">The object to mask.</param>
    /// <returns>A new object with masked sensitive fields.</returns>
    public T MaskSensitiveProperties<T>(T obj) where T : class, new()
    {
        if (obj == null)
            return new T();

        var type = typeof(T);
        var properties = type.GetProperties();
        var masked = Activator.CreateInstance<T>();

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite)
                continue;

            var value = prop.GetValue(obj);
            if (value == null)
            {
                prop.SetValue(masked, null);
                continue;
            }

            if (IsSensitiveField(prop.Name) && value is string strValue)
            {
                var maskedValue = MaskValue(prop.Name, strValue);
                prop.SetValue(masked, maskedValue);
            }
            else
            {
                prop.SetValue(masked, value);
            }
        }

        return masked;
    }
}
