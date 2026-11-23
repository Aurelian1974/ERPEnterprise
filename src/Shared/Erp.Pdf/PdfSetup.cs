using QuestPDF.Infrastructure;

namespace Erp.Pdf;

/// <summary>
/// Pornirea QuestPDF: tipul licentei (Community, Professional, Enterprise) vine din configurarea instalarii (Pdf:QuestPdfLicense), implicit
/// Community. Fara licenta declarata, QuestPDF refuza generarea. Se apeleaza o singura data, la pornirea gazdei (Erp.Host, Erp.Worker).
/// </summary>
public static class PdfSetup
{
    public const string ConfigurationKey = "Pdf:QuestPdfLicense";

    public static LicenseType Configure(string? license)
    {
        var type = string.IsNullOrWhiteSpace(license)
            ? LicenseType.Community
            : Enum.TryParse<LicenseType>(license.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new InvalidOperationException($"Setarea {ConfigurationKey} are valoarea „{license}”; valorile permise: Community, Professional, Enterprise.");
        QuestPDF.Settings.License = type;
        return type;
    }
}
