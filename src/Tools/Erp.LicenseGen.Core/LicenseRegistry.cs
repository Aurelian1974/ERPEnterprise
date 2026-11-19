using System.Globalization;
using System.Text;
using Erp.Licensing;

namespace Erp.LicenseGen.Core;

/// <summary>Un rand din registrul licentelor emise (08, 3.6). Nu contine chei.</summary>
public sealed record IssuedLicense(
    string LicenseId, DateOnly IssuedAt, string CustomerName, string FiscalCodes, string Modules, string Limits,
    string ValidUntil, string UpdatesUntil, byte KeyId, string FileName);

/// <summary>
/// %APPDATA%\ErpLicenseGen\licente-emise.csv (08, 3.6): UTF-8 cu BOM si separator „;”, ca sa se deschida corect in Excel
/// cu setarile romanesti. Din el se calculeaza urmatorul numar de licenta.
/// </summary>
public sealed class LicenseRegistry(string path)
{
    private const string Header = "Număr licență;Data;Client;CUI-uri;Module;Limite;Valabilă până la;Actualizări incluse până la;KeyId;Fișier";

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ErpLicenseGen", "licente-emise.csv");

    public string Path { get; } = path;

    public IReadOnlyList<IssuedLicense> ReadAll()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        return File.ReadAllLines(Path, Encoding.UTF8).Skip(1).Where(l => l.Length > 0).Select(Parse).ToList();
    }

    /// <summary>LIC-&lt;AAAA&gt;-&lt;NNNNNN&gt;: anul emiterii si urmatorul numar de ordine din registru (unic pe toti anii).</summary>
    public string NextLicenseId(DateOnly today)
    {
        var last = ReadAll().Select(l => int.TryParse(l.LicenseId.AsSpan(l.LicenseId.Length - 6), CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return $"LIC-{today.Year:D4}-{last + 1:D6}";
    }

    public void Append(License license, byte keyId, string fileName)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var line = string.Join(';', new[]
        {
            license.LicenseId,
            license.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            license.CustomerName,
            license.FiscalCodes,
            string.Join(',', license.Modules),
            $"{license.Limits.Users}/{license.Limits.PayrollEmployees}/{license.Limits.PosTerminals}",
            license.ValidUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "nelimitat",
            license.UpdatesUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "fără limită",
            keyId.ToString(CultureInfo.InvariantCulture),
            fileName,
        }.Select(Quote));

        var prefix = File.Exists(Path) ? string.Empty : Header + Environment.NewLine;
        File.AppendAllText(Path, prefix + line + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Quote(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    private static IssuedLicense Parse(string line)
    {
        var fields = Split(line);
        return new IssuedLicense(fields[0], DateOnly.ParseExact(fields[1], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], byte.Parse(fields[8], CultureInfo.InvariantCulture), fields[9]);
    }

    private static List<string> Split(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ';' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
