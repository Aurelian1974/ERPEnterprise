using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Core.Application.Users;

/// <summary>Cand pleaca emailul unei notificari: imediat sau in rezumatul zilnic (02.01, 11.2: „imediat sau rezumat zilnic”).</summary>
public static class EmailModes
{
    public const string Immediate = "Immediate";
    public const string DailyDigest = "DailyDigest";

    public static readonly IReadOnlyList<string> All = [Immediate, DailyDigest];
}

/// <summary>
/// Un tip de notificare din catalog. Critical = nu se poate opri in aplicatie (02.01, 11.2), doar pe email. Implicitele se aplica
/// utilizatorului care nu si-a ales preferintele: in aplicatie toate, email imediat doar pentru cele critice (decizia din 21.11.2026).
/// </summary>
public sealed record NotificationTypeDto(string Code, string ModuleCode, bool Critical, bool DefaultInApp, bool DefaultEmail, string DefaultEmailMode);

/// <summary>Preferinta efectiva a utilizatorului pentru un tip; IsDefault = utilizatorul nu a ales nimic pentru tipul acesta.</summary>
public sealed record NotificationPreferenceDto(string Code, bool InApp, bool Email, string EmailMode, bool IsDefault);

/// <summary>Alegerea trimisa la salvare pentru un tip de notificare.</summary>
public sealed record NotificationPreferenceInput(string Code, bool InApp, bool Email, string EmailMode);

/// <summary>
/// Catalogul tipurilor de notificare (02.01, 4.7). Acum doar tipurile Nucleului (4.7 pasul 4 si 3.4); fiecare modul isi adauga
/// tipurile odata cu notificarile lui. Codurile sunt tehnice si stabile: se salveaza in preferintele utilizatorilor.
/// </summary>
public static class NotificationTypes
{
    public const string ExternalAccessExpiring = "Core.ExternalAccessExpiring";
    public const string AccountLocked = "Core.AccountLocked";
    public const string LicenseExpiring = "Core.LicenseExpiring";
    public const string OutboxFailed = "Core.OutboxFailed";
    public const string ImportCompleted = "Core.ImportCompleted";

    public static readonly IReadOnlyList<NotificationTypeDto> All =
    [
        Normal(ExternalAccessExpiring),
        Critical(AccountLocked),
        Critical(LicenseExpiring),
        Critical(OutboxFailed),
        Normal(ImportCompleted),
    ];

    public static NotificationTypeDto? Find(string code) => All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));

    private static NotificationTypeDto Normal(string code) => new(code, "Core", false, true, false, EmailModes.DailyDigest);

    private static NotificationTypeDto Critical(string code) => new(code, "Core", true, true, true, EmailModes.Immediate);
}

/// <summary>
/// Preferintele de notificare in User.NotificationPreferences (singura sursa, decizia din 21.11.2026), JSON cu versiune:
/// {"version":1,"types":{"Core.AccountLocked":{"inApp":true,"email":false,"emailMode":"Immediate"}}}. Se pastreaza doar alegerile
/// facute; un tip fara alegere primeste implicitele catalogului, iar tipurile disparute din catalog se ignora.
/// </summary>
public static class NotificationPreferenceRules
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Preferintele efective pe tot catalogul, din JSON-ul salvat (null sau invalid = implicitele).</summary>
    public static IReadOnlyList<NotificationPreferenceDto> Effective(string? json)
    {
        var saved = Parse(json);
        return [.. NotificationTypes.All.Select(type => saved.TryGetValue(type.Code, out var choice)
            ? new NotificationPreferenceDto(type.Code, type.Critical || choice.InApp, choice.Email,
                EmailModes.All.Contains(choice.EmailMode) ? choice.EmailMode! : type.DefaultEmailMode, false)
            : new NotificationPreferenceDto(type.Code, type.DefaultInApp, type.DefaultEmail, type.DefaultEmailMode, true))];
    }

    /// <summary>Erorile pe tip ale alegerilor trimise (cheia = „notificationPreferences.{cod}”); goala = valide.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(IReadOnlyList<NotificationPreferenceInput> input)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var group in input.GroupBy(p => p.Code, StringComparer.Ordinal))
        {
            var key = $"notificationPreferences.{group.Key}";
            var type = NotificationTypes.Find(group.Key);
            var item = group.First();
            if (type is null)
            {
                errors[key] = ["Tipul de notificare nu există."];
            }
            else if (group.Count() > 1)
            {
                errors[key] = ["Tipul de notificare apare de două ori."];
            }
            else if (type.Critical && !item.InApp)
            {
                errors[key] = ["Notificările critice nu se pot opri în aplicație, doar pe email."];
            }
            else if (!EmailModes.All.Contains(item.EmailMode))
            {
                errors[key] = ["Alegeți când pleacă emailul: imediat sau în rezumatul zilnic."];
            }
        }

        return errors;
    }

    /// <summary>JSON-ul de salvat: doar tipurile din catalog, in ordinea lui.</summary>
    public static string Serialize(IReadOnlyList<NotificationPreferenceInput> input)
    {
        var types = NotificationTypes.All
            .Select(type => input.FirstOrDefault(p => string.Equals(p.Code, type.Code, StringComparison.Ordinal)))
            .Where(p => p is not null)
            .ToDictionary(p => p!.Code, p => new Choice(p!.InApp, p.Email, p.EmailMode), StringComparer.Ordinal);
        return JsonSerializer.Serialize(new Document(Version, types), Json);
    }

    private static Dictionary<string, Choice> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(StringComparer.Ordinal);
        }

        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, Json);
            return document?.Types is { } types ? new(types, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private sealed record Document(int Version, Dictionary<string, Choice>? Types);

    private sealed record Choice(bool InApp, bool Email, string? EmailMode);
}
