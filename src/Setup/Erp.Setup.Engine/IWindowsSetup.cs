using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Health;
using Erp.Setup.Core.Kit;
using Erp.Setup.Windows.Certificates;

namespace Erp.Setup.Engine;

/// <summary>
/// Operatiile pe serverul de aplicatie folosite de plan (IIS, serviciu, fisiere, certificate). Implementarea reala e
/// <see cref="WindowsSetup"/>; testele folosesc un fals, ca planul si pasii SQL sa ruleze si pe Linux.
/// </summary>
public interface IWindowsSetup
{
    /// <summary>I-01: folderele din C:\ProgramData\Erp\&lt;Instanta&gt; (5.7).</summary>
    void CreateDataFolders(InstallRequest request);

    /// <summary>I-02: functiile IIS lipsa (nume DISM).</summary>
    IReadOnlyList<string> MissingIisFeatures();

    /// <returns>true daca activarea cere repornirea calculatorului (DISM 3010).</returns>
    bool EnableIisFeatures(IReadOnlyList<string> missing);

    /// <summary>I-03: Hosting Bundle cel putin la versiunea din manifest, plus modulul ASP.NET Core din IIS.</summary>
    bool IsHostingBundleInstalled(string minimumVersion);

    /// <returns>true daca instalarea cere repornirea calculatorului.</returns>
    bool InstallHostingBundle(string kitRoot);

    /// <summary>I-04: certificatul intern (LocalMachine\My si Root, plus certificat-erp.cer); intoarce amprenta.</summary>
    string CreateInternalHttpsCertificate(InstallRequest request);

    CertificateInfo? FindMachineCertificate(string thumbprint);

    /// <summary>I-05: copiaza programul in versions\&lt;Versiune&gt;\{host,worker} si verifica hash-urile; intoarce cate fisiere s-au copiat.</summary>
    int DeployBinaries(string kitRoot, KitManifest manifest, InstallRequest request);

    /// <summary>I-06: pool-ul si site-ul IIS (oprite) si serviciul Windows (oprit); pentru gMSA, dreptul „Log on as a service”.</summary>
    void CreateSiteAndService(InstallRequest request, string version, string certificateThumbprint);

    /// <summary>I-07: configurarea fara secrete, web.config, paginile de mentenanta si drepturile NTFS.</summary>
    void WriteConfiguration(string kitRoot, InstallRequest request, string version, InstallationSettingsValues values);

    /// <summary>
    /// I-08: certificatul de protectie a datelor; daca <paramref name="existingThumbprint"/> e in LocalMachine\My se pastreaza
    /// („Repară” nu il recreeaza). Reaplica dreptul de citire pe cheia privata. Intoarce amprenta.
    /// </summary>
    string EnsureDataProtectionCertificate(InstallRequest request, string? existingThumbprint);

    /// <summary>Contul calculatorului in retea (DOMENIU\CALCULATOR$), pentru identitatea ComputerAccount (5.8).</summary>
    string ComputerAccount();

    /// <summary>I-15: regulile de firewall.</summary>
    void ApplyFirewall(InstallRequest request);

    /// <summary>I-17: porneste serviciul si site-ul.</summary>
    void StartApplication(InstallRequest request, string version);

    /// <summary>I-17: health-check-ul din 5.14.</summary>
    Task<HealthCheckOutcome> CheckHealthAsync(InstallRequest request, string version, string? lastMigration, string? pinnedThumbprint, CancellationToken ct);

    /// <summary>Ultimele randuri din jurnalele Host si Worker, pentru mesajul de eroare de la I-17.</summary>
    IReadOnlyList<string> ComponentLogTail(InstallRequest request);

    /// <summary>Parola administratorului la instalarea neasistata (5.10, D29); intoarce calea fisierului.</summary>
    string WriteInitialAdminFile(InstallRequest request, string content);

    /// <summary>Cheia de recuperare (5.12, 11.4).</summary>
    void ExportRecoveryKey(string thumbprint, string pfxPath, string password);

    RecoveryKeyCheck VerifyRecoveryKey(string pfxPath, string password, string? expectedThumbprint);
}
