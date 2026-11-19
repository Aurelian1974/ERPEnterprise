using Erp.Configuration;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Kit;
using Erp.Setup.Windows.Services;

namespace Erp.Setup.Windows.Layout;

/// <summary>
/// Pasul I-05 (5.11) si actualizarea: copiaza app\host si app\worker din kit in versions\&lt;Versiune&gt;\{host,worker}, apoi
/// recalculeaza SHA-256 pentru fiecare fisier si il compara cu manifestul. Fisierele deja corecte nu se recopiaza (reluare).
/// </summary>
public static class BinaryDeployer
{
    private static readonly (string KitFolder, string Target)[] Components = [("app/host/", "host"), ("app/worker/", "worker")];

    /// <param name="replaceExisting">U-09: folderul unei incercari esuate se sterge si se recopiaza.</param>
    public static int Deploy(string kitRoot, KitManifest manifest, InstanceLayout layout, bool replaceExisting)
    {
        var versionDirectory = Path.Combine(layout.VersionsDirectory, manifest.Version);
        if (replaceExisting && Directory.Exists(versionDirectory))
        {
            Directory.Delete(versionDirectory, recursive: true);
        }

        var copied = 0;
        var problems = new List<string>();
        foreach (var file in manifest.Files)
        {
            var component = Components.FirstOrDefault(c => file.Path.StartsWith(c.KitFolder, StringComparison.Ordinal));
            if (component.KitFolder is null)
            {
                continue;
            }

            var relative = file.Path[component.KitFolder.Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(versionDirectory, component.Target, relative);
            if (!IsValid(target, file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(kitRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)), target, overwrite: true);
                copied++;
            }

            if (!IsValid(target, file))
            {
                problems.Add(file.Path);
            }
        }

        if (problems.Count > 0)
        {
            throw new WindowsSetupException($"Binarele copiate nu corespund manifestului: {string.Join(", ", problems)}.");
        }

        return copied;
    }

    private static bool IsValid(string path, KitFile file)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == file.Size && string.Equals(KitVerifier.ComputeSha256(path), file.Sha256, StringComparison.Ordinal);
    }
}

/// <summary>
/// Pasul I-07 (5.11) si actualizarea: appsettings.Installation.json (din sablonul kitului, pastrand valorile existente, fara
/// secrete), web.config in folderul host al versiunii si paginile de mentenanta (maintenance.html, app_offline.template.htm).
/// </summary>
public static class ConfigurationWriter
{
    public const string TemplateRelativePath = "templates/appsettings.Installation.json";
    public const string MaintenancePageRelativePath = "templates/maintenance.html";
    public const string AppOfflineTemplateRelativePath = "templates/app_offline.template.htm";

    public static void Write(string kitRoot, InstanceLayout layout, string version, InstallationSettingsValues values)
    {
        var data = layout.Data;
        Directory.CreateDirectory(data.ConfigDirectory);
        Directory.CreateDirectory(data.MaintenanceDirectory);

        var template = File.ReadAllText(KitPath(kitRoot, TemplateRelativePath));
        var existing = File.Exists(data.AppSettings) ? File.ReadAllText(data.AppSettings) : null;
        WriteAtomic(data.AppSettings, AppSettingsGenerator.Generate(template, existing, values));

        CopyTemplate(kitRoot, MaintenancePageRelativePath, data.MaintenancePage);
        CopyTemplate(kitRoot, AppOfflineTemplateRelativePath, data.AppOfflineTemplate);

        var hostDirectory = layout.HostDirectory(version);
        Directory.CreateDirectory(hostDirectory);
        WriteAtomic(Path.Combine(hostDirectory, "web.config"), WebConfigGenerator.Generate(data.InstanceDirectory));
    }

    private static void CopyTemplate(string kitRoot, string relativePath, string target)
    {
        var source = KitPath(kitRoot, relativePath);
        if (!File.Exists(source))
        {
            throw new WindowsSetupException($"Kitul nu conține {relativePath}.");
        }

        var temporary = target + ".tmp";
        File.Copy(source, temporary, overwrite: true);
        File.Move(temporary, target, overwrite: true);
    }

    private static string KitPath(string kitRoot, string relativePath) =>
        Path.Combine(kitRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
