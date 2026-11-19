using Erp.Setup.Core.Kit;

namespace Erp.Setup.Cli;

/// <summary>Cheile publice ale kitului incorporate la build (08, 7).</summary>
internal static class KitPublicKey
{
    public static IReadOnlyDictionary<int, string> Load() => KitPublicKeys.FromAssembly(typeof(KitPublicKey).Assembly, "Erp.Setup.Cli");
}
