using Isopoh.Cryptography.Argon2;
using Microsoft.AspNetCore.Identity;
using System.Text;

namespace ValyanERP.Web.Infrastructure.Identity;

/// <summary>
/// Custom password hasher using Argon2id algorithm.
/// Argon2id is the recommended algorithm for password hashing (PHC winner 2015).
/// It provides protection against GPU cracking and side-channel attacks.
/// </summary>
public class Argon2PasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : class
{
    // Argon2id configuration - OWASP recommended settings
    private const int MemoryCost = 65536;    // 64 MB - memory usage
    private const int TimeCost = 3;          // 3 iterations
    private const int Parallelism = 4;       // 4 parallel threads
    private const int HashLength = 32;       // 256-bit hash output

    /// <summary>
    /// Hashes a password using Argon2id algorithm.
    /// </summary>
    public string HashPassword(TUser user, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));

        var config = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,  // Argon2id = HybridAddressing
            Version = Argon2Version.Nineteen,
            MemoryCost = MemoryCost,
            TimeCost = TimeCost,
            Lanes = Parallelism,
            Threads = Parallelism,
            HashLength = HashLength,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = GenerateSalt()
        };

        using var argon2 = new Argon2(config);
        using var hash = argon2.Hash();
        
        return config.EncodeString(hash.Buffer);
    }

    /// <summary>
    /// Verifies a password against a stored hash.
    /// Supports both Argon2id and legacy ASP.NET Identity hashes for migration.
    /// </summary>
    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword))
            return PasswordVerificationResult.Failed;
        
        if (string.IsNullOrEmpty(providedPassword))
            return PasswordVerificationResult.Failed;

        // Check if this is an Argon2 hash (starts with $argon2)
        if (hashedPassword.StartsWith("$argon2"))
        {
            try
            {
                if (Argon2.Verify(hashedPassword, providedPassword))
                {
                    return PasswordVerificationResult.Success;
                }
                return PasswordVerificationResult.Failed;
            }
            catch
            {
                return PasswordVerificationResult.Failed;
            }
        }

        // Legacy ASP.NET Identity hash (base64 encoded, starts with specific bytes)
        // Format: {0x00 or 0x01}{salt}{hash} for V2/V3 format
        try
        {
            var legacyHasher = new PasswordHasher<TUser>();
            var result = legacyHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
            
            if (result == PasswordVerificationResult.Success)
            {
                // Password is correct but uses old format - suggest rehash
                return PasswordVerificationResult.SuccessRehashNeeded;
            }
            
            return result;
        }
        catch
        {
            return PasswordVerificationResult.Failed;
        }
    }

    /// <summary>
    /// Generates a cryptographically secure random salt.
    /// </summary>
    private static byte[] GenerateSalt()
    {
        var salt = new byte[16]; // 128-bit salt
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }
}
