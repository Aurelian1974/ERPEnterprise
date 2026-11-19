using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Erp.LicenseGen.Core;

/// <summary>Protectia cheilor private pe calculatorul de emitere (08, 3.5).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}

/// <summary>DPAPI CurrentUser: cheile se pot citi doar de contul Windows care le-a salvat, pe acest calculator.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCurrentUserProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "Erp.LicenseGen.Keys.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

public sealed class KeyStoreException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Rezultatul primei configurari: textul pentru LicenseKeys.cs si unde s-a scris copia de siguranta.</summary>
public sealed record KeySetupResult(string SourceLines, string BackupPath);

/// <summary>
/// Cheile de emitere (08, 3.5, decizia D16): %APPDATA%\ErpLicenseGen\keys\sign-&lt;KeyId&gt;.bin, protejate cu DPAPI.
/// La prima configurare se genereaza cheia activa 1 si cheia de rezerva 2; ambele merg in copia de siguranta (criptata cu
/// parola), dar pe calculator ramane doar cheia 1.
/// </summary>
public sealed class KeyStore(string folder, ISecretProtector protector)
{
    public const byte ActiveKeyId = 1;
    public const byte ReserveKeyId = 2;
    public const int MinimumBackupPasswordLength = 12;
    private const int Pbkdf2Iterations = 600_000;
    private const string BackupMarker = "ERPKEYS1";

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ErpLicenseGen", "keys");

    /// <summary>KeyId-urile salvate pe acest calculator.</summary>
    public IReadOnlyList<byte> StoredKeyIds => !Directory.Exists(folder) ? []
        : Directory.EnumerateFiles(folder, "sign-*.bin")
            .Select(f => byte.TryParse(Path.GetFileNameWithoutExtension(f)["sign-".Length..], out var id) ? id : (byte)0)
            .Where(id => id != 0)
            .Order()
            .ToList();

    /// <summary>Cheia folosita la emitere: cea mai mare salvata (dupa o rotire, cheia noua).</summary>
    public LicenseSigningKey? LoadActive() => StoredKeyIds.Count == 0 ? null : Load(StoredKeyIds[^1]);

    public LicenseSigningKey Load(byte keyId)
    {
        var path = PathFor(keyId);
        if (!File.Exists(path))
        {
            throw new KeyStoreException($"Cheia {keyId} nu există pe acest calculator.");
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredKey>(protector.Unprotect(File.ReadAllBytes(path)))
                ?? throw new KeyStoreException($"Cheia {keyId} este deteriorată.");
            return LicenseSigningKey.Import(stored.KeyId, stored.PrivateKey, stored.AesKey);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            throw new KeyStoreException($"Cheia {keyId} nu se poate citi cu contul Windows curent.", ex);
        }
    }

    /// <summary>
    /// Prima configurare (butonul „Chei”): genereaza cheile 1 si 2, scrie copia de siguranta cu amandoua, pastreaza local
    /// doar cheia 1 si intoarce liniile pentru LicenseKeys.cs. Refuza daca exista deja chei, ca sa nu fie inlocuite din greseala.
    /// </summary>
    public KeySetupResult Setup(string backupPath, string backupPassword)
    {
        if (StoredKeyIds.Count > 0)
        {
            throw new KeyStoreException("Cheile sunt deja configurate pe acest calculator. Pentru restaurare folosiți „Importă din copia de siguranță”.");
        }

        using var active = LicenseSigningKey.Generate(ActiveKeyId);
        using var reserve = LicenseSigningKey.Generate(ReserveKeyId);
        WriteBackup(backupPath, backupPassword, [active, reserve]);
        Save(active);
        return new KeySetupResult(active.ToSourceLine() + Environment.NewLine + reserve.ToSourceLine(), backupPath);
    }

    /// <summary>Restaurarea sau rotirea (08, 3.5): aduce pe calculator cheia ceruta din copia de siguranta.</summary>
    public void ImportFromBackup(string backupPath, string backupPassword, byte keyId)
    {
        var keys = ReadBackup(backupPath, backupPassword);
        var key = keys.FirstOrDefault(k => k.KeyId == keyId)
            ?? throw new KeyStoreException($"Copia de siguranță nu conține cheia {keyId}.");
        using var signing = LicenseSigningKey.Import(key.KeyId, key.PrivateKey, key.AesKey);
        Save(signing);
    }

    public static void WriteBackup(string path, string password, IReadOnlyList<LicenseSigningKey> keys)
    {
        if (password.Length < MinimumBackupPasswordLength)
        {
            throw new KeyStoreException($"Parola copiei de siguranță trebuie să aibă cel puțin {MinimumBackupPasswordLength} caractere.");
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(keys.Select(k => new StoredKey(k.KeyId, k.ExportPkcs8PrivateKey(), k.AesKey)).ToList());
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(password, salt);
        var cipher = new byte[json.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, json, cipher, tag, Encoding.ASCII.GetBytes(BackupMarker));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
            CryptographicOperations.ZeroMemory(key);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes(BackupMarker), .. salt, .. nonce, .. tag, .. cipher]);
    }

    public static IReadOnlyList<StoredKey> ReadBackup(string path, string password)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 8 + 16 + 12 + 16 || Encoding.ASCII.GetString(data, 0, 8) != BackupMarker)
        {
            throw new KeyStoreException("Fișierul nu este o copie de siguranță a cheilor ErpLicenseGen.");
        }

        var key = DeriveKey(password, data.AsSpan(8, 16).ToArray());
        var plain = new byte[data.Length - 52];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(data.AsSpan(24, 12), data.AsSpan(52), data.AsSpan(36, 16), plain, Encoding.ASCII.GetBytes(BackupMarker));
            return JsonSerializer.Deserialize<List<StoredKey>>(plain) ?? [];
        }
        catch (CryptographicException ex)
        {
            throw new KeyStoreException("Parola este greșită sau copia de siguranță este deteriorată.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private void Save(LicenseSigningKey key)
    {
        Directory.CreateDirectory(folder);
        var json = JsonSerializer.SerializeToUtf8Bytes(new StoredKey(key.KeyId, key.ExportPkcs8PrivateKey(), key.AesKey));
        try
        {
            File.WriteAllBytes(PathFor(key.KeyId), protector.Protect(json));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private string PathFor(byte keyId) => Path.Combine(folder, $"sign-{keyId}.bin");

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);

    public sealed record StoredKey(byte KeyId, byte[] PrivateKey, byte[] AesKey);
}
