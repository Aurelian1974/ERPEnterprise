using Erp.LicenseGen.Core;
using Erp.Licensing;

namespace Erp.LicenseGen.Tests;

/// <summary>Chei generate in test (activa 1, de rezerva 2) si o licenta corecta (08, 3.3), cu CUI-uri valide.</summary>
internal sealed class TestLicenses : IDisposable
{
    public const string FiscalCodes = "10000008:12345674:23456783:34567892";

    public LicenseSigningKey Active { get; } = LicenseSigningKey.Generate(1);

    public LicenseSigningKey Reserve { get; } = LicenseSigningKey.Generate(2);

    public LicenseKeyRing Ring => new([Active.ToPublicKey(), Reserve.ToPublicKey()]);

    public LicenseReader Reader => new(Ring, TimeProvider.System);

    public static License Sample(DateOnly? validUntil = null, DateOnly? updatesUntil = null, string fiscalCodes = FiscalCodes,
        string product = License.ProductCode, DateOnly? issuedAt = null, string licenseId = "LIC-2026-000123") => new()
    {
        LicenseId = licenseId,
        Product = product,
        CustomerName = "Grupul Exemplu",
        FiscalCodes = fiscalCodes,
        Modules = ["Core", "Finance", "Sales"],
        Limits = new LicenseLimits(100, 300, 10),
        IssuedAt = issuedAt ?? new DateOnly(2026, 11, 15),
        ValidUntil = validUntil,
        UpdatesUntil = updatesUntil,
    };

    public void Dispose()
    {
        Active.Dispose();
        Reserve.Dispose();
    }
}
