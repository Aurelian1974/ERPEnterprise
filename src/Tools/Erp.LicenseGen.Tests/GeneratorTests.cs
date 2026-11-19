using Erp.LicenseGen.Core;
using Erp.Licensing;

namespace Erp.LicenseGen.Tests;

/// <summary>Protectie simulata (pe Linux nu exista DPAPI); DPAPI se verifica pe Windows.</summary>
internal sealed class XorProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

    public byte[] Unprotect(byte[] data) => Protect(data);
}

internal sealed class FakeDialogs : IGeneratorDialogs
{
    public string? SavePath { get; set; }

    public string? OpenPath { get; set; }

    public string? BackupPath { get; set; }

    public string? Password { get; set; } = "parola-de-test-lunga";

    public bool? KeyAction { get; set; } = true;

    public byte? KeyId { get; set; }

    public List<string> Errors { get; } = [];

    public List<string> Infos { get; } = [];

    public List<string> Texts { get; } = [];

    public string? SuggestedName { get; private set; }

    public string? AskSaveLicensePath(string suggestedFileName)
    {
        SuggestedName = suggestedFileName;
        return SavePath;
    }

    public string? AskOpenLicensePath() => OpenPath;

    public string? AskSaveBackupPath() => BackupPath;

    public string? AskOpenBackupPath() => BackupPath;

    public string? AskNewPassword(string title) => Password;

    public string? AskPassword(string title) => Password;

    public bool? AskKeyAction() => KeyAction;

    public byte? AskKeyId() => KeyId;

    public void ShowInfo(string title, string message) => Infos.Add(message);

    public void ShowError(string title, string message) => Errors.Add(message);

    public void ShowText(string title, string text) => Texts.Add(text);

    public void OpenWithDefaultApp(string path)
    {
    }
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public sealed class GeneratorTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("erp-licgen-").FullName;
    private readonly FakeDialogs dialogs = new();
    private readonly TimeProvider time = new FixedTime(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private KeyStore Keys => new(Path.Combine(folder, "keys"), new XorProtector());

    private LicenseRegistry Registry => new(Path.Combine(folder, "licente-emise.csv"));

    private GeneratorViewModel ViewModel() => new(Keys, Registry, dialogs, time);

    private void SetupKeys()
    {
        dialogs.BackupPath = Path.Combine(folder, "backup.erpkeys");
        ViewModel().ManageKeysCommand.Execute(null);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Fiscal_codes_are_normalized_to_digits_separated_by_colon()
    {
        var entries = FiscalCodeInput.Parse("RO10000008, ro 12345674");

        Assert.Equal("10000008:12345674", FiscalCodeInput.Normalized(entries));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void All_accepted_separators_and_ro_with_a_space_are_understood()
    {
        var entries = FiscalCodeInput.Parse("RO 10000008\n12345674; 23456783:34567892");

        Assert.Equal("10000008:12345674:23456783:34567892", FiscalCodeInput.Normalized(entries));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Wrong_check_digit_and_duplicates_block_generation()
    {
        var entries = FiscalCodeInput.Parse("10000001, RO10000008, 10000008");

        Assert.Null(FiscalCodeInput.Normalized(entries));
        Assert.Equal("cifra de control este greșită", entries[0].Problem);
        Assert.Null(entries[1].Problem);
        Assert.Equal("apare de mai multe ori", entries[2].Problem);
    }

    [Fact]
    public void License_numbers_follow_the_register()
    {
        var registry = Registry;
        Assert.Equal("LIC-2026-000001", registry.NextLicenseId(new DateOnly(2026, 11, 20)));

        registry.Append(TestLicenses.Sample() with { LicenseId = "LIC-2026-000001", CustomerName = "Firma; cu \"ghilimele\"" }, 1, "a.lic");

        Assert.Equal("LIC-2027-000002", registry.NextLicenseId(new DateOnly(2027, 1, 2)));
        Assert.Equal("Firma; cu \"ghilimele\"", Assert.Single(registry.ReadAll()).CustomerName);
    }

    [Fact]
    public void Key_setup_keeps_only_the_active_key_and_backs_up_both()
    {
        SetupKeys();

        Assert.Equal([KeyStore.ActiveKeyId], Keys.StoredKeyIds);
        var backup = KeyStore.ReadBackup(dialogs.BackupPath!, dialogs.Password!);
        Assert.Equal([1, 2], backup.Select(k => (int)k.KeyId));
        var text = Assert.Single(dialogs.Texts);
        Assert.Contains("new(1, ", text, StringComparison.Ordinal);
        Assert.Contains("new(2, ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_setup_is_refused_so_keys_are_never_replaced_by_mistake()
    {
        SetupKeys();

        ViewModel().ManageKeysCommand.Execute(null);

        Assert.Contains(dialogs.Errors, e => e.Contains("deja configurate", StringComparison.Ordinal));
    }

    [Fact]
    public void Reserve_key_is_imported_from_the_backup_for_rotation()
    {
        SetupKeys();
        dialogs.KeyAction = false;
        dialogs.KeyId = 2;

        ViewModel().ManageKeysCommand.Execute(null);

        using var active = Keys.LoadActive();
        Assert.Equal(2, active!.KeyId);
    }

    [Fact]
    public void Backup_with_a_wrong_password_is_refused()
    {
        SetupKeys();

        var ex = Assert.Throws<KeyStoreException>(() => KeyStore.ReadBackup(dialogs.BackupPath!, "alta-parola-lunga"));

        Assert.Equal("Parola este greșită sau copia de siguranță este deteriorată.", ex.Message);
    }

    [Fact]
    public void Short_backup_password_is_refused()
    {
        dialogs.Password = "scurta";
        SetupKeys();

        Assert.Empty(Keys.StoredKeyIds);
        Assert.Contains(dialogs.Errors, e => e.Contains("cel puțin 12", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Generated_license_opens_with_the_same_checks_as_the_erp()
    {
        SetupKeys();
        var vm = ViewModel();
        vm.CustomerName = "Grupul Exemplu";
        vm.FiscalCodesText = "RO10000008\nRO 12345674";
        dialogs.SavePath = Path.Combine(folder, "out.lic");

        Assert.True(vm.GenerateCommand.CanExecute(null));
        vm.GenerateCommand.Execute(null);

        Assert.Empty(dialogs.Errors);
        Assert.Equal("Grupul Exemplu-LIC-2026-000001.lic", dialogs.SuggestedName);
        using var key = Keys.LoadActive();
        var result = new LicenseReader(new LicenseKeyRing([key!.ToPublicKey()]), time).Read(File.ReadAllBytes(dialogs.SavePath));
        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal("10000008:12345674", result.License!.FiscalCodes);
        Assert.Contains(LicenseModules.Core, result.License.Modules);
        Assert.Equal(new DateOnly(2027, 11, 20), result.License.ValidUntil);
        Assert.Null(result.License.UpdatesUntil);
        Assert.Equal("LIC-2026-000002", vm.LicenseNumber);
        Assert.Single(Registry.ReadAll());
    }

    [Fact]
    public void Open_license_shows_the_content_of_a_valid_file()
    {
        SetupKeys();
        using (var key = Keys.LoadActive())
        {
            dialogs.OpenPath = Path.Combine(folder, "in.lic");
            File.WriteAllBytes(dialogs.OpenPath, LicenseWriter.Write(TestLicenses.Sample(), key!));
        }

        ViewModel().OpenLicenseCommand.Execute(null);

        Assert.Empty(dialogs.Errors);
        Assert.Contains("RO10000008, RO12345674", Assert.Single(dialogs.Infos), StringComparison.Ordinal);
    }

    [Fact]
    public void Generation_needs_a_key_and_valid_input()
    {
        var vm = ViewModel();
        vm.CustomerName = "Grupul Exemplu";
        vm.FiscalCodesText = "10000008";

        Assert.False(vm.GenerateCommand.CanExecute(null));
        Assert.Equal("Firmă", vm.LicenseType);
    }

    [Fact]
    public void Updates_cannot_extend_beyond_the_license_validity()
    {
        var vm = ViewModel();
        vm.CustomerName = "Grupul Exemplu";
        vm.FiscalCodesText = "10000008";
        vm.UpdatesUnlimited = false;
        vm.UpdatesUntil = new DateTime(2028, 1, 1);

        Assert.Equal("Actualizările incluse nu pot depăși valabilitatea licenței.", vm.BuildLicense().Error);
    }

    [Fact]
    public void Core_module_is_always_included_and_locked()
    {
        var vm = ViewModel();
        var core = vm.Modules.Single(m => m.Code == LicenseModules.Core);
        core.IsSelected = false;
        vm.CustomerName = "Grupul Exemplu";
        vm.FiscalCodesText = "10000008";

        Assert.True(core.IsLocked);
        Assert.Contains(LicenseModules.Core, vm.BuildLicense().License!.Modules);
    }
}
