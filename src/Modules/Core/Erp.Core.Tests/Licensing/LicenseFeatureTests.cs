using Erp.Core.Application;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Licensing;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Licensing;
using Erp.LicenseGen.Core;
using Erp.Licensing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static Erp.Core.Tests.Pipeline.PipelineTests;

namespace Erp.Core.Tests.Licensing;

/// <summary>Incarcarea licentei din aplicatie (08, 4.1, 4.2) si starea calculata din ea (08, 4.4).</summary>
public sealed class LicenseFeatureTests : IDisposable
{
    private readonly LicenseSigningKey key = LicenseSigningKey.Generate(1);
    private readonly FakeContext context = new();
    private readonly FakeLicense license = new();
    private readonly FakeLicenseFeature feature = new();
    private readonly FakeUnitOfWork unitOfWork = new(new FakeTransient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => key.Dispose();

    private static License Sample(DateOnly issuedAt, string licenseId = "LIC-2026-000123", DateOnly? validUntil = null) => new()
    {
        LicenseId = licenseId,
        Product = License.ProductCode,
        CustomerName = "Grupul Exemplu",
        FiscalCodes = "10000008:12345674",
        Modules = ["Core", "Finance"],
        Limits = new LicenseLimits(100, 300, 10),
        IssuedAt = issuedAt,
        ValidUntil = validUntil,
    };

    private ISender Sender()
    {
        context.Permissions.Add(CorePermissions.LicenseManage);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(typeof(LicenseFeatureTests).Assembly);
        services.AddSingleton<ICurrentContext>(context);
        services.AddSingleton<Application.Me.ICurrentUserProfile>(context);
        services.AddSingleton<ILicenseInfo>(license);
        services.AddSingleton<ILicenseSnapshot>(feature);
        services.AddSingleton<ILicenseStore>(feature);
        services.AddSingleton<ILicenseRefresh>(feature);
        services.AddSingleton<ILicenseReader>(new LicenseReader(new LicenseKeyRing([key.ToPublicKey()]), TimeProvider.System));
        services.AddSingleton<Application.Organization.IOrganizationStore>(new FakeOrganizationStore());
        services.AddSingleton<IPeriodLock>(new FakePeriods());
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<IIdempotencyStore>(unitOfWork);
        services.AddSingleton<IOutbox>(unitOfWork);
        services.AddSingleton<IAuditWriter>(unitOfWork);
        services.AddSingleton<ITransientFailureDetector>(new FakeTransient());
        services.AddSingleton<ISqlErrorTranslator, NoSqlErrors>();
        services.AddScoped<HandlerCalls>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true })
            .CreateScope().ServiceProvider.GetRequiredService<ISender>();
    }

    private string Base64(License l) => Convert.ToBase64String(LicenseWriter.Write(l, key));

    [Fact]
    public async Task Valid_license_is_saved_with_its_bytes_and_the_cache_is_refreshed()
    {
        var file = Base64(Sample(new DateOnly(2026, 11, 15)));

        var result = await Sender().Send(new UploadLicense(file), Ct);

        var saved = Assert.Single(feature.Saved);
        Assert.Equal(Convert.FromBase64String(file), saved.File);
        Assert.Equal(1, feature.RefreshRequests);
        Assert.Equal(["RO10000008", "RO12345674"], result.FiscalCodes);
        Assert.Equal("1", result.KeyId);
    }

    [Fact]
    public async Task Upload_is_allowed_when_the_installation_is_read_only()
    {
        license.State = LicenseState.ReadOnly;

        await Sender().Send(new UploadLicense(Base64(Sample(new DateOnly(2026, 11, 15)))), Ct);

        Assert.Single(feature.Saved);
    }

    [Theory]
    [InlineData("nu-e-base64!")]
    [InlineData("AAAA")]
    public async Task File_that_is_not_a_license_is_refused_on_the_field(string content)
    {
        var ex = await Assert.ThrowsAsync<ErpValidationException>(() => Sender().Send(new UploadLicense(content), Ct));

        Assert.Equal(["Fișierul nu este o licență ERP."], ex.Errors["fileBase64"]);
        Assert.Empty(feature.Saved);
    }

    [Fact]
    public async Task Expired_license_is_refused()
    {
        var expired = Base64(Sample(new DateOnly(2025, 1, 1), validUntil: new DateOnly(2025, 12, 31)));

        var ex = await Assert.ThrowsAsync<ErpValidationException>(() => Sender().Send(new UploadLicense(expired), Ct));

        Assert.StartsWith("Licența a expirat la 31.12.2025.", ex.Errors["fileBase64"][0], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-15")]
    public async Task Older_license_than_the_active_one_is_refused_with_409()
    {
        feature.Current = Sample(new DateOnly(2026, 11, 15));
        var older = Base64(Sample(new DateOnly(2026, 10, 1), "LIC-2026-000100"));

        var ex = await Assert.ThrowsAsync<ErpException>(() => Sender().Send(new UploadLicense(older), Ct));

        Assert.Equal(ErpErrorKind.Conflict, ex.Kind);
        Assert.Equal(CoreErrorCodes.LicenseOlder, ex.Code);
        Assert.Empty(feature.Saved);
    }

    [Fact]
    public async Task Upload_needs_the_license_permission()
    {
        var sender = Sender();
        context.Permissions.Remove(CorePermissions.LicenseManage);

        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new UploadLicense("AAAA"), Ct));

        Assert.Equal(ErpErrorKind.Forbidden, ex.Kind);
    }
}

/// <summary>Starea calculata din licenta (08, 4.4) si firmele nelicentiate (08, 4.3 pasul 7).</summary>
public sealed class LicenseInfoTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly Clock Today = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    private static License Sample(DateOnly? validUntil) => new()
    {
        LicenseId = "LIC-2026-000123",
        Product = License.ProductCode,
        CustomerName = "Grupul Exemplu",
        FiscalCodes = "10000008",
        Modules = ["Core", "Finance", "Robotics"],
        Limits = new LicenseLimits(100, 300, 10),
        IssuedAt = new DateOnly(2026, 1, 1),
        ValidUntil = validUntil,
    };

    private static LicenseInfo Info(bool development = false) => new(new LicenseOptions(development), Today, NullLogger<LicenseInfo>.Instance);

    [Fact]
    public void Without_a_license_only_core_is_active_and_the_installation_is_read_only()
    {
        var info = Info();

        Assert.False(info.HasLicense);
        Assert.Equal(LicenseState.ReadOnly, info.State);
        Assert.Equal(["Core"], info.ActiveModules);
        Assert.False(info.IsFiscalCodeLicensed("10000008"));
    }

    [Fact]
    public void Development_without_a_license_activates_everything()
    {
        var info = Info(development: true);

        Assert.Equal(LicenseState.Valid, info.State);
        Assert.True(info.IsModuleActive("Sales"));
        Assert.True(info.IsFiscalCodeLicensed("12345674"));
    }

    [Fact]
    public void Unlimited_license_is_always_valid()
    {
        var info = Info();
        info.Apply(new LicenseSnapshotData(Sample(validUntil: null), 1, new DateOnly(2026, 11, 20), false, new HashSet<int>()));

        Assert.Equal(LicenseState.Valid, info.State);
        Assert.Null(info.ValidUntil);
        Assert.Equal(["Core", "Finance"], info.ActiveModules.Order(StringComparer.Ordinal));
        Assert.True(info.IsFiscalCodeLicensed("RO 10000008"));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-15")]
    public void Clock_set_back_does_not_extend_the_license()
    {
        var info = Info();

        // Ceasul arata 20.11.2026, dar aplicatia a vazut deja 10.01.2027: licenta expirata la 31.12.2026 e in gratie, nu valida.
        info.Apply(new LicenseSnapshotData(Sample(new DateOnly(2026, 12, 31)), 1, new DateOnly(2027, 1, 10), true, new HashSet<int>()));

        Assert.Equal(new DateOnly(2027, 1, 10), info.ReferenceDate);
        Assert.Equal(LicenseState.Grace, info.State);
        Assert.True(info.ClockBehind);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-07")]
    public void Active_company_missing_from_the_license_is_not_licensed()
    {
        var info = Info();
        info.Apply(new LicenseSnapshotData(Sample(null), 1, new DateOnly(2026, 11, 20), false, new HashSet<int> { 7 }));

        Assert.False(info.IsCompanyLicensed(7));
        Assert.True(info.IsCompanyLicensed(8));
    }
}
