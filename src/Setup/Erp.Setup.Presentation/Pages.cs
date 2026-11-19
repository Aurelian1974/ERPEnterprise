using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;

namespace Erp.Setup.Presentation;

/// <summary>Ecranele asistentului (08, 5.3).</summary>
public enum WizardPage
{
    Welcome,
    Database,
    Site,
    License,
    Checks,
    Install,
    Done,

    /// <summary>Actiunile pe o instalare existenta (ecranul 0): actualizare, reparare, resetare, cheie, diagnostic, dezinstalare.</summary>
    Action,
}

public enum ActionKind
{
    Upgrade,
    Rollback,
    Repair,
    ResetAdministrator,
    RecoveryKey,
    Diagnose,
    Uninstall,
}

/// <summary>Un rand din lista pasilor din stanga.</summary>
public sealed partial class PageItem(WizardPage page, string title) : ObservableObject
{
    public WizardPage Page { get; } = page;

    public string Title { get; } = title;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}

/// <summary>O instalare gasita pe calculator (ecranul 0).</summary>
public sealed record InstallationItem(InstanceState State, string KitVersion)
{
    public string Instance => State.Instance;

    public string Summary => $"{State.Instance} · {State.CurrentVersion ?? "—"} · {StateText}";

    public string StateText => State.State switch
    {
        InstanceLifecycle.Installed => "Instalată",
        InstanceLifecycle.Installing => "Instalare întreruptă",
        InstanceLifecycle.Upgrading => "Actualizare întreruptă",
        InstanceLifecycle.RollingBack => "Revenire întreruptă",
        _ => "În mentenanță",
    };

    public string Address => $"https://{State.HostName}{(State.HttpsPort is null or 443 ? string.Empty : $":{State.HttpsPort}")}/";

    public string RecoveryKey => State.RecoveryKeyExportedAt is { } at ? $"exportată la {at.ToLocalTime():dd.MM.yyyy}" : "neexportată";

    public bool CanResume => State.State == InstanceLifecycle.Installing;

    /// <summary>Actualizare intrerupta: singura actiune e „Revino la versiunea anterioară” (10.2, cod 73).</summary>
    public bool IsUpgradeInterrupted => State.State is InstanceLifecycle.Upgrading or InstanceLifecycle.RollingBack;

    /// <summary>Actiunile obisnuite (actualizare, reparare, cheie, diagnostic…) pe o instalare terminata sau in mentenanta.</summary>
    public bool CanManage => State.State is InstanceLifecycle.Installed or InstanceLifecycle.Maintenance;

    public bool CanUpgrade => State.State == InstanceLifecycle.Installed && Version.TryParse(State.CurrentVersion, out var current)
        && Version.TryParse(KitVersion, out var kit) && kit > current;
}

/// <summary>Un rand din lista verificarilor (ecranul 4).</summary>
public sealed record CheckRow(CheckResult Result)
{
    public string Id => Result.Id;

    public string Title => Result.Title;

    public string Message => Result.ExitCode is { } code && Result.Status == CheckStatus.Failed ? $"{Result.Message} (cod {code})" : Result.Message;

    public string Symbol => Result.Status switch
    {
        CheckStatus.Passed => "✓",
        CheckStatus.Warning => "⚠",
        CheckStatus.Failed => "✗",
        _ => "–",
    };

    public CheckStatus Status => Result.Status;
}

public enum StepRowStatus
{
    Pending,
    Running,
    Done,
    Warning,
    Failed,
    Skipped,
}

/// <summary>Un pas din planul afisat pe ecranul 5.</summary>
public sealed partial class StepRow(string id, string title) : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Symbol))]
    public partial StepRowStatus Status { get; set; }

    [ObservableProperty]
    public partial string? Duration { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public string Symbol => Status switch
    {
        StepRowStatus.Pending => "○",
        StepRowStatus.Running => "…",
        StepRowStatus.Done => "✓",
        StepRowStatus.Warning => "⚠",
        StepRowStatus.Failed => "✗",
        _ => "–",
    };
}

/// <summary>Valorile ecranului 1 pentru alegerea identitatii aplicatiei pe SQL la distanta (5.8).</summary>
public sealed record IdentityOption(AppIdentity Identity, string Title);

public static class Options
{
    public static readonly IReadOnlyList<IdentityOption> RemoteIdentities =
    [
        new(AppIdentity.Gmsa, "Cont gMSA (recomandat)"),
        new(AppIdentity.ComputerAccount, "Contul calculatorului ⚠"),
    ];

    public static readonly IReadOnlyList<DayOfWeek> Days =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static ObservableCollection<PageItem> Pages() =>
    [
        new(WizardPage.Welcome, "Bun venit"),
        new(WizardPage.Database, "Baza de date"),
        new(WizardPage.Site, "Site web"),
        new(WizardPage.License, "Licență"),
        new(WizardPage.Checks, "Verificare"),
        new(WizardPage.Install, "Instalare"),
        new(WizardPage.Done, "Gata"),
    ];
}
