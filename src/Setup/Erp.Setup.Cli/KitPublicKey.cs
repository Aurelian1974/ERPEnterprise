namespace Erp.Setup.Cli;

/// <summary>Cheia publica a kitului, incorporata la build (-p:KitPublicKeyFile, 08, 7).</summary>
internal static class KitPublicKey
{
    public const string ResourceName = "Erp.Setup.Cli.KitPublicKey.pem";

    public static string? Load()
    {
        using var stream = typeof(KitPublicKey).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
