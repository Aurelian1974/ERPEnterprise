using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace Erp.Security.Passwords;

/// <summary>
/// Hash-ul parolelor in formatul ASP.NET Core Identity V3 (PasswordHasher&lt;TUser&gt; cu optiunile implicite), acelasi pe care
/// il verifica Erp.Auth (8.8). Parola in clar nu ajunge in SQL.
/// </summary>
public static class IdentityPasswordHasher
{
    private static readonly PasswordHasher<HashSubject> Hasher = new();
    private static readonly HashSubject Subject = new();

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return Hasher.HashPassword(Subject, password);
    }

    public static bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;

    // PasswordHasher nu foloseste utilizatorul in formatul V3; tipul exista doar pentru semnatura generica.
    private sealed class HashSubject;
}

/// <summary>Stampile contului (8.8, punctul 4).</summary>
public static class AccountStamps
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>SecurityStamp: 20 de octeti aleatori in Base32 (RFC 4648), 32 de caractere, ca UserManager din ASP.NET Core Identity.</summary>
    public static string NewSecurityStamp() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public static string NewConcurrencyStamp() => Guid.NewGuid().ToString();

    internal static string ToBase32(ReadOnlySpan<byte> data)
    {
        var result = new char[(data.Length * 8 + 4) / 5];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var value in data)
        {
            buffer = ((buffer << 8) | value) & 0xFFFF;
            bits += 8;
            while (bits >= 5)
            {
                result[index++] = Base32Alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            result[index++] = Base32Alphabet[(buffer << (5 - bits)) & 31];
        }

        return new string(result, 0, index);
    }
}
