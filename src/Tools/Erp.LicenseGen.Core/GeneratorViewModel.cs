using System.Collections.ObjectModel;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erp.Licensing;

namespace Erp.LicenseGen.Core;

/// <summary>Dialogurile ferestrei; implementarea WPF sta in Erp.LicenseGen, testele folosesc una simulata.</summary>
public interface IGeneratorDialogs
{
    string? AskSaveLicensePath(string suggestedFileName);

    string? AskOpenLicensePath();

    string? AskSaveBackupPath();

    string? AskOpenBackupPath();

    /// <summary>Parola noua, ceruta de doua ori; null daca operatorul renunta.</summary>
    string? AskNewPassword(string title);

    string? AskPassword(string title);

    /// <summary>Configurare noua (true) sau import din copia de siguranta (false); null = renunta.</summary>
    bool? AskKeyAction();

    byte? AskKeyId();

    void ShowInfo(string title, string message);

    void ShowError(string title, string message);

    /// <summary>Text de copiat (liniile pentru LicenseKeys.cs).</summary>
    void ShowText(string title, string text);

    /// <summary>Deschide fisierul cu aplicatia implicita (registrul, in Excel).</summary>
    void OpenWithDefaultApp(string path);
}

/// <summary>Un modul din licenta, cu numele afisat (08, 3.2).</summary>
public sealed partial class ModuleOption(string code, string name, bool isSelected, bool isLocked) : ObservableObject
{
    public string Code { get; } = code;

    public string Name { get; } = name;

    public bool IsLocked { get; } = isLocked;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}

/// <summary>Fereastra generatorului de licente (08, 3.2). Toata logica e aici, testabila fara fereastra (R1).</summary>
public sealed partial class GeneratorViewModel : ObservableObject
{
    private static readonly (string Code, string Name, bool Default)[] ModuleList =
    [
        ("Core", "Nucleu", true), ("Finance", "Finanțe și contabilitate", true), ("Sales", "Vânzări", true),
        ("Purchasing", "Achiziții", true), ("Inventory", "Stocuri", true), ("Wms", "WMS", false), ("Tms", "Transport (TMS)", false),
        ("Manufacturing", "Producție", false), ("Hr", "HR și salarizare", false), ("Pos", "POS", false),
        ("Construction", "Construcții", false), ("Crm", "CRM", false), ("Reporting", "Raportare", true),
        ("Integrations", "Integrări ANAF și BNR", true),
    ];

    private readonly KeyStore keys;
    private readonly LicenseRegistry registry;
    private readonly IGeneratorDialogs dialogs;
    private readonly TimeProvider time;

    public GeneratorViewModel(KeyStore keys, LicenseRegistry registry, IGeneratorDialogs dialogs, TimeProvider time)
    {
        this.keys = keys;
        this.registry = registry;
        this.dialogs = dialogs;
        this.time = time;
        Modules = new ObservableCollection<ModuleOption>(ModuleList.Select(m => new ModuleOption(m.Code, m.Name, m.Default, m.Code == LicenseModules.Core)));
        foreach (var module in Modules)
        {
            module.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ModulesSummary));
        }

        ValidUntil = Today.AddYears(1).ToDateTime(TimeOnly.MinValue);
        Refresh();
    }

    public ObservableCollection<ModuleOption> Modules { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial string FiscalCodesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Users { get; set; } = 100;

    [ObservableProperty]
    public partial int PayrollEmployees { get; set; } = 300;

    [ObservableProperty]
    public partial int PosTerminals { get; set; } = 10;

    [ObservableProperty]
    public partial DateTime? ValidUntil { get; set; }

    [ObservableProperty]
    public partial bool IsUnlimited { get; set; }

    [ObservableProperty]
    public partial DateTime? UpdatesUntil { get; set; }

    [ObservableProperty]
    public partial bool UpdatesUnlimited { get; set; } = true;

    [ObservableProperty]
    public partial string LicenseNumber { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string KeyStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial bool HasKey { get; private set; }

    public IReadOnlyList<FiscalCodeEntry> FiscalCodeEntries => FiscalCodeInput.Parse(FiscalCodesText);

    public string? NormalizedFiscalCodes => FiscalCodeInput.Normalized(FiscalCodeEntries);

    public string FiscalCodeSummary
    {
        get
        {
            var entries = FiscalCodeEntries;
            var bad = entries.Where(e => !e.IsAccepted).ToList();
            return entries.Count == 0 ? "Scrieți cel puțin un CUI."
                : bad.Count == 0 ? $"✓ {entries.Count} {(entries.Count == 1 ? "CUI valid" : "CUI-uri valide")}"
                : "✗ " + string.Join("; ", bad.Select(e => $"{e.Input}: {e.Problem}"));
        }
    }

    public string LicenseType => FiscalCodeEntries.Count(e => e.IsAccepted) switch
    {
        0 => "—",
        1 => "Firmă",
        var n => $"Grup de {n} firme",
    };

    public string ModulesSummary => $"{Modules.Count(m => m.IsSelected)} din {Modules.Count}";

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>Licenta din valorile ferestrei, sau motivul pentru care nu se poate genera.</summary>
    public (License? License, string? Error) BuildLicense()
    {
        var name = CustomerName.Trim();
        if (name.Length is < 2 or > 200)
        {
            return (null, "Denumirea clientului trebuie să aibă 2–200 de caractere.");
        }

        if (NormalizedFiscalCodes is not { } codes)
        {
            return (null, "Corectați lista de CUI-uri: " + FiscalCodeSummary);
        }

        if (Users < 0 || PayrollEmployees < 0 || PosTerminals < 0)
        {
            return (null, "Limitele nu pot fi negative.");
        }

        DateOnly? validUntil = IsUnlimited ? null : ValidUntil is { } v ? DateOnly.FromDateTime(v) : null;
        if (!IsUnlimited && validUntil is null)
        {
            return (null, "Alegeți data de valabilitate sau bifați „Nelimitat”.");
        }

        if (validUntil is { } until && until < Today)
        {
            return (null, "Data de valabilitate este în trecut.");
        }

        DateOnly? updatesUntil = UpdatesUnlimited ? null : UpdatesUntil is { } u ? DateOnly.FromDateTime(u) : null;
        if (!UpdatesUnlimited && updatesUntil is null)
        {
            return (null, "Alegeți data pentru actualizări sau bifați „Fără limită”.");
        }

        if (updatesUntil is { } upd && validUntil is { } val && upd > val)
        {
            return (null, "Actualizările incluse nu pot depăși valabilitatea licenței.");
        }

        var license = new License
        {
            LicenseId = LicenseNumber,
            Product = License.ProductCode,
            CustomerName = name,
            FiscalCodes = codes,
            Modules = Modules.Where(m => m.IsSelected || m.IsLocked).Select(m => m.Code).ToList(),
            Limits = new LicenseLimits(Users, PayrollEmployees, PosTerminals),
            IssuedAt = Today,
            ValidUntil = validUntil,
            UpdatesUntil = updatesUntil,
        };
        return license.Validate() is { } problem ? (null, $"Licența nu respectă regulile: {problem}.") : (license, null);
    }

    private bool CanGenerate() => HasKey && CustomerName.Trim().Length >= 2 && NormalizedFiscalCodes is not null;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void Generate()
    {
        var (license, error) = BuildLicense();
        if (license is null)
        {
            dialogs.ShowError("Licența nu se poate genera", error!);
            return;
        }

        LicenseSigningKey? key;
        try
        {
            key = keys.LoadActive();
        }
        catch (KeyStoreException ex)
        {
            dialogs.ShowError("Cheia nu se poate citi", ex.Message + Environment.NewLine + "Restaurați cheia cu „Chei” → „Importă din copia de siguranță”.");
            return;
        }

        using var _ = key;
        if (key is null)
        {
            dialogs.ShowError("Lipsește cheia", "Configurați cheile cu butonul „Chei”.");
            return;
        }

        var path = dialogs.AskSaveLicensePath($"{SafeFileName(license.CustomerName)}-{license.LicenseId}.lic");
        if (path is null)
        {
            return;
        }

        File.WriteAllBytes(path, LicenseWriter.Write(license, key));
        registry.Append(license, key.KeyId, Path.GetFileName(path));
        dialogs.ShowInfo("Licența a fost generată", $"{license.LicenseId} · {license.CustomerName} · {license.Companies} CUI-uri.{Environment.NewLine}{path}");
        Refresh();
    }

    [RelayCommand]
    private void OpenLicense()
    {
        var path = dialogs.AskOpenLicensePath();
        if (path is null)
        {
            return;
        }

        var result = new LicenseReader(ReaderKeys(), time).Read(File.ReadAllBytes(path));
        if (!result.IsAuthentic)
        {
            dialogs.ShowError("Licența nu este validă", result.Message);
            return;
        }

        dialogs.ShowInfo(result.IsValid ? "Licență validă" : "Licență expirată", Describe(result));
    }

    [RelayCommand]
    private void ManageKeys()
    {
        try
        {
            var setup = dialogs.AskKeyAction();
            if (setup is true)
            {
                var backupPath = dialogs.AskSaveBackupPath();
                var password = backupPath is null ? null : dialogs.AskNewPassword("Parola copiei de siguranță a cheilor");
                if (backupPath is null || password is null)
                {
                    return;
                }

                var result = keys.Setup(backupPath, password);
                dialogs.ShowText(
                    "Cheile au fost generate",
                    "Copiați liniile în src/Shared/Erp.Licensing/LicenseKeys.cs (lista Entries) și faceți commit." + Environment.NewLine
                    + $"Copia de siguranță cu cheile 1 și 2: {result.BackupPath}. Copiați-o pe două suporturi offline; parola, separat." + Environment.NewLine
                    + Environment.NewLine + result.SourceLines);
            }
            else if (setup is false)
            {
                var backupPath = dialogs.AskOpenBackupPath();
                var password = backupPath is null ? null : dialogs.AskPassword("Parola copiei de siguranță");
                var keyId = password is null ? null : dialogs.AskKeyId();
                if (backupPath is null || password is null || keyId is null)
                {
                    return;
                }

                keys.ImportFromBackup(backupPath, password, keyId.Value);
                dialogs.ShowInfo("Cheia a fost importată", $"Licențele noi se semnează cu cheia {keyId}.");
            }
        }
        catch (KeyStoreException ex)
        {
            dialogs.ShowError("Cheile", ex.Message);
        }

        Refresh();
    }

    [RelayCommand]
    private void OpenRegistry()
    {
        if (!File.Exists(registry.Path))
        {
            dialogs.ShowInfo("Registrul licențelor", "Nu a fost emisă încă nicio licență de pe acest calculator.");
            return;
        }

        dialogs.OpenWithDefaultApp(registry.Path);
    }

    private LicenseKeyRing ReaderKeys()
    {
        var all = new Dictionary<byte, LicenseKey>();
        foreach (var id in LicenseKeys.Embedded.KeyIds)
        {
            all[id] = LicenseKeys.Embedded.Find(id)!;
        }

        LicenseSigningKey? local = null;
        try
        {
            local = keys.LoadActive();
        }
        catch (KeyStoreException)
        {
            // Cheia locala nu se poate citi: se folosesc doar cheile publice incorporate.
        }

        using (local)
        {
            if (local is not null)
            {
                all[local.KeyId] = local.ToPublicKey();
            }
        }

        return new LicenseKeyRing(all.Values);
    }

    private static string Describe(LicenseReadResult result)
    {
        var l = result.License!;
        var culture = CultureInfo.InvariantCulture;
        return string.Join(Environment.NewLine,
            $"Număr: {l.LicenseId} (cheia {result.KeyId})",
            $"Client: {l.CustomerName}",
            $"CUI-uri: {string.Join(", ", l.FiscalCodeList.Select(c => "RO" + c))}",
            $"Module: {string.Join(", ", l.Modules)}",
            $"Limite: {l.Limits.Users} utilizatori · {l.Limits.PayrollEmployees} angajați · {l.Limits.PosTerminals} case POS",
            $"Emisă: {l.IssuedAt.ToString("dd.MM.yyyy", culture)}",
            $"Valabilă până la: {l.ValidUntil?.ToString("dd.MM.yyyy", culture) ?? "nelimitat"}",
            $"Actualizări incluse până la: {l.UpdatesUntil?.ToString("dd.MM.yyyy", culture) ?? "fără limită"}");
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '_' : c)).Trim();

    private void Refresh()
    {
        LicenseNumber = registry.NextLicenseId(Today);
        var stored = keys.StoredKeyIds;
        HasKey = stored.Count > 0;
        KeyStatus = HasKey
            ? $"Cheia de semnare {stored[^1]} (activă) · {registry.ReadAll().Count} licențe emise"
            : "Nu există chei pe acest calculator: folosiți „Chei”.";
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(FiscalCodesText))
        {
            OnPropertyChanged(nameof(FiscalCodeEntries));
            OnPropertyChanged(nameof(NormalizedFiscalCodes));
            OnPropertyChanged(nameof(FiscalCodeSummary));
            OnPropertyChanged(nameof(LicenseType));
        }
    }
}
