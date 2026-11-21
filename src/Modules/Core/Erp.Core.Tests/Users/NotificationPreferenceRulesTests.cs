using Erp.Core.Application.Users;

namespace Erp.Core.Tests.Users;

/// <summary>Preferintele de notificare (02.01, 4.7 si 11.2): implicitele catalogului, tipurile critice, JSON-ul salvat.</summary>
public sealed class NotificationPreferenceRulesTests
{
    [Fact]
    [Trait("Criteriu", "02.01-11.2")]
    public void Without_choices_everything_is_in_app_and_only_critical_types_go_by_email()
    {
        var effective = NotificationPreferenceRules.Effective(null);

        Assert.Equal(NotificationTypes.All.Count, effective.Count);
        Assert.All(effective, p => Assert.True(p.InApp && p.IsDefault));
        Assert.Equal(NotificationTypes.All.Where(t => t.Critical).Select(t => t.Code), effective.Where(p => p.Email).Select(p => p.Code));
    }

    [Fact]
    [Trait("Criteriu", "02.01-11.2")]
    public void Saved_choices_round_trip_and_unknown_or_missing_types_fall_back_to_defaults()
    {
        var json = NotificationPreferenceRules.Serialize(
            [new(NotificationTypes.ImportCompleted, false, true, EmailModes.Immediate), new("Sales.Removed", true, true, EmailModes.Immediate)]);

        Assert.DoesNotContain("Sales.Removed", json, StringComparison.Ordinal);
        var effective = NotificationPreferenceRules.Effective(json);
        var import = Assert.Single(effective, p => p.Code == NotificationTypes.ImportCompleted);
        Assert.Equal((false, true, EmailModes.Immediate, false), (import.InApp, import.Email, import.EmailMode, import.IsDefault));
        Assert.True(Assert.Single(effective, p => p.Code == NotificationTypes.AccountLocked).IsDefault);
        Assert.All(NotificationPreferenceRules.Effective("nu e json"), p => Assert.True(p.IsDefault));
    }

    [Fact]
    [Trait("Criteriu", "02.01-11.2")]
    public void Critical_types_cannot_be_turned_off_in_the_application()
    {
        var errors = NotificationPreferenceRules.Validate(
        [
            new(NotificationTypes.AccountLocked, false, true, EmailModes.Immediate),
            new("Core.Unknown", true, false, EmailModes.Immediate),
            new(NotificationTypes.ImportCompleted, true, true, "Weekly"),
        ]);

        Assert.Equal(["notificationPreferences.Core.AccountLocked", "notificationPreferences.Core.ImportCompleted", "notificationPreferences.Core.Unknown"],
            errors.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(NotificationPreferenceRules.Validate([new(NotificationTypes.AccountLocked, true, false, EmailModes.DailyDigest)]));
    }
}
