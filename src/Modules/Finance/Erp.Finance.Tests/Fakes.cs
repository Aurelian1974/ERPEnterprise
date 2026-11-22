using Erp.Core.Contracts;

namespace Erp.Finance.Tests;

/// <summary>Contextul cererii in teste: utilizatorul, firma curenta si permisiunile date explicit.</summary>
public sealed class FakeContext : ICurrentContext
{
    public bool IsAuthenticated { get; set; } = true;

    public int UserId { get; set; } = 1;

    public int CompanyId { get; set; } = 1;

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public string? IdempotencyKey { get; set; }

    public string? IpAddress { get; set; } = "127.0.0.1";

    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

    public bool HasPermission(string permission, DimensionRef? scope = null) => IsAuthenticated && Permissions.Contains(permission);
}

/// <summary>Licenta in teste: modulele Core si Finance active.</summary>
public sealed class FakeLicense : ILicenseInfo
{
    public HashSet<string> Modules { get; } = new(StringComparer.Ordinal) { "Core", "Finance" };

    public bool HasLicense => true;

    public bool IsModuleActive(string moduleCode) => Modules.Contains(moduleCode);

    public IReadOnlyCollection<string> ActiveModules => Modules;

    public bool IsFiscalCodeLicensed(string fiscalCode) => true;

    public bool IsCompanyLicensed(int companyId) => true;

    public int? GetLimit(string limitCode) => null;

    public DateOnly? ValidUntil => new(2099, 12, 31);

    public DateOnly? UpdatesUntil => null;

    public bool ClockBehind => false;

    public string? CustomerName => "Grupul Exemplu";

    public LicenseState State => LicenseState.Valid;
}

/// <summary>Perioadele contabile inchise ale testului (zilele date); in rest, perioada e deschisa.</summary>
public sealed class FakePeriodLock(IReadOnlySet<DateOnly> closedDays) : IPeriodLock
{
    public Task<bool> IsClosedAsync(int companyId, DateOnly date, CancellationToken ct) => Task.FromResult(closedDays.Contains(date));
}
