using System.Security.Cryptography;
using System.Text;

namespace Erp.Setup.Sql.Database;

/// <summary>
/// Amprenta continutului unui script (08, 5.9): SHA-256 hex, dupa normalizarea care nu schimba sensul — marcajul BOM,
/// sfarsiturile de rand (CRLF/CR → LF), spatiile si tab-urile de la capatul randurilor si randurile goale de la final.
/// O reformatare a fisierului nu produce o versiune noua; orice alt caracter, inclusiv dintr-un comentariu, da.
/// </summary>
public static class ScriptFingerprint
{
    public static string Compute(string contents)
    {
        var normalized = Normalize(contents);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string Normalize(string contents)
    {
        var text = contents.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = text.Split('\n');
        var builder = new StringBuilder(text.Length);
        foreach (var line in lines)
        {
            builder.Append(line.TrimEnd(' ', '\t')).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }
}
