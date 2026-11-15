using System.Security.AccessControl;
using System.Security.Principal;
using Erp.Security.Passwords;
using Erp.Setup.Core.Secrets;
using Erp.Setup.Windows.Security;
using Microsoft.AspNetCore.Identity;

namespace Erp.Setup.Tests.Administrator;

public sealed class InitialAdminUnitTests
{
    [Fact]
    public void Generated_initial_password_has_20_unambiguous_characters_and_meets_the_policy()
    {
        var passwords = Enumerable.Range(0, 200).Select(_ => InitialPasswordGenerator.Generate()).ToList();

        Assert.All(passwords, p =>
        {
            Assert.Equal(20, p.Length);
            Assert.All(p, c => Assert.Contains(c, InitialPasswordGenerator.Alphabet));
            Assert.DoesNotContain(p, c => "Il1O0o".Contains(c));
            Assert.True(p.Count(char.IsUpper) >= 2 && p.Count(char.IsLower) >= 2 && p.Count(char.IsDigit) >= 2);
            Assert.True(p.Count(c => "!#$%&*+-=?@_".Contains(c)) >= 2);
            Assert.Empty(InitialPasswordPolicy.Validate(p, "administrator"));
        });
        Assert.Equal(passwords.Count, passwords.Distinct().Count());
    }

    [Fact]
    public void Password_hash_is_identity_v3_and_verified_by_the_identity_hasher()
    {
        const string password = "Wq7#Hn2p+Kd9!mXa4Rt_";

        var hash = IdentityPasswordHasher.Hash(password);

        Assert.Equal(0x01, Convert.FromBase64String(hash)[0]); // marcajul formatului V3
        Assert.NotEqual(hash, IdentityPasswordHasher.Hash(password));
        Assert.True(IdentityPasswordHasher.Verify(hash, password));
        Assert.False(IdentityPasswordHasher.Verify(hash, password.ToUpperInvariant()));
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<object>().VerifyHashedPassword(new object(), hash, password));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_follows_rfc4648_without_padding(string input, string expected)
    {
        Assert.Equal(expected, AccountStamps.ToBase32(System.Text.Encoding.ASCII.GetBytes(input)));
    }

    [Fact]
    public void Security_stamp_is_32_random_bytes_in_base32()
    {
        var stamp = AccountStamps.NewSecurityStamp();

        Assert.Equal(32, stamp.Length);
        Assert.Matches("^[A-Z2-7]{32}$", stamp);
        Assert.NotEqual(stamp, AccountStamps.NewSecurityStamp());
        Assert.True(Guid.TryParse(AccountStamps.NewConcurrencyStamp(), out _));
    }

    [Fact]
    public void Summary_shows_the_password_once_or_a_reset_hint_when_skipped()
    {
        var created = InitialAdminSummary.Render("https://erp.grupulexemplu.local/", "administrator", "Wq7#Hn2p+Kd9!mXa4Rt_");
        var skipped = InitialAdminSummary.Render("https://erp.grupulexemplu.local/", "administrator", null);

        Assert.Contains("Parolă temporară:    Wq7#Hn2p+Kd9!mXa4Rt_   (afișată O SINGURĂ DATĂ)", created);
        Assert.Contains("parolă nouă și activarea 2FA", created);
        Assert.DoesNotContain("Parolă temporară", skipped);
        Assert.Contains("reset-admin", skipped);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-05")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Initial_admin_file_is_restricted_to_administrators_and_system()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("ACL NTFS: doar pe Windows.");
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "erp-admin-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, InitialAdminFile.FileName);
        try
        {
            InitialAdminFile.Write(path, "administrator / parola");
            InitialAdminFile.Write(path, "administrator / alta parola");

            var security = new FileInfo(path).GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            Assert.True(security.AreAccessRulesProtected);
            Assert.All(rules, r => Assert.False(r.IsInherited));
            Assert.Equal(
                new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) }.OrderBy(s => s.Value),
                rules.Select(r => (SecurityIdentifier)r.IdentityReference).OrderBy(s => s.Value));
            Assert.All(rules, r => Assert.Equal(FileSystemRights.FullControl, r.FileSystemRights & FileSystemRights.FullControl));
            Assert.Equal(27, new FileInfo(path).Length);

            using var current = WindowsIdentity.GetCurrent();
            InitialAdminFile.GrantDelete(path, current.User!);
            var withDelete = new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
            Assert.Contains(withDelete, r => r.IdentityReference == current.User && r.FileSystemRights.HasFlag(FileSystemRights.Delete));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
