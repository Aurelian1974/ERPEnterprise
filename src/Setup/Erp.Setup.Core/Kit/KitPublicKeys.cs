using System.Reflection;

namespace Erp.Setup.Core.Kit;

/// <summary>
/// Cheile publice ale kitului incorporate in installer la build (08, 7): -p:KitPublicKey1File (activa) si -p:KitPublicKey2File
/// (de rezerva) devin resursele „&lt;prefix&gt;.KitPublicKey.1.pem” si „.2.pem”.
/// </summary>
public static class KitPublicKeys
{
    public static IReadOnlyDictionary<int, string> FromAssembly(Assembly assembly, string resourcePrefix)
    {
        var keys = new Dictionary<int, string>();
        foreach (var id in new[] { 1, 2 })
        {
            using var stream = assembly.GetManifestResourceStream($"{resourcePrefix}.KitPublicKey.{id}.pem");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                keys[id] = reader.ReadToEnd();
            }
        }

        return keys;
    }
}
