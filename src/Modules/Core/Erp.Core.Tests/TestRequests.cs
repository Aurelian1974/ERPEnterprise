using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests;

// Cereri de test pentru pipeline: aceleasi atribute ca in modulele reale (NET-04).

[Module("Core")]
[RequiresPermission("Core.Settings.Manage")]
[Audited("Core.Test.Ping")]
public sealed record PingCommand(string Text, bool Fail = false) : ICommand<PingResult>;

public sealed record PingResult(Guid Id, string Text);

public sealed record PingHappened(string Text) : IntegrationEvent;

public sealed class PingCommandValidator : AbstractValidator<PingCommand>
{
    public PingCommandValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(20);
}

/// <summary>Scrie un eveniment in outbox si descrie audit-ul; cu Fail = true arunca dupa scriere (proba rollback-ului, P8).</summary>
public sealed class PingCommandHandler(IOutbox outbox, IAuditScope audit, HandlerCalls calls) : ICommandHandler<PingCommand, PingResult>
{
    public async Task<PingResult> Handle(PingCommand request, CancellationToken cancellationToken)
    {
        calls.Count++;
        await outbox.EnqueueAsync(new PingHappened(request.Text), cancellationToken);
        audit.Describe("Core.Test", 42, new { request.Text });
        if (request.Fail)
        {
            throw ErpException.Conflict("CORE_TEST_FAILED", "Eșec simulat după scriere.");
        }

        return new PingResult(Guid.NewGuid(), request.Text);
    }
}

[Module("Core")]
[RequiresPermission("Core.Settings.Manage")]
public sealed record OtherCommand : ICommand<int>;

public sealed class OtherCommandHandler : ICommandHandler<OtherCommand, int>
{
    public Task<int> Handle(OtherCommand request, CancellationToken cancellationToken) => Task.FromResult(7);
}

[Module("Core")]
[RequiresPermission("Core.Settings.Manage")]
public sealed record PostDocument(DateOnly AccountingDate) : ICommand<bool>, IAccountingDated;

public sealed class PostDocumentHandler(HandlerCalls calls) : ICommandHandler<PostDocument, bool>
{
    public Task<bool> Handle(PostDocument request, CancellationToken cancellationToken)
    {
        calls.Count++;
        return Task.FromResult(true);
    }
}

[Module("Sales")]
[RequiresPermission("Sales.Invoice.View")]
public sealed record GetInvoice(int Id) : IQuery<int>;

public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, int>
{
    public Task<int> Handle(GetInvoice request, CancellationToken cancellationToken) => Task.FromResult(request.Id);
}

[Module("Core")]
[RequiresPermission("Core.Partner.View")]
public sealed record ListThings : IQuery<int>;

public sealed class ListThingsHandler(HandlerCalls calls) : IQueryHandler<ListThings, int>
{
    public Task<int> Handle(ListThings request, CancellationToken cancellationToken)
    {
        calls.Count++;
        return Task.FromResult(3);
    }
}

/// <summary>Cerere fara permisiune declarata: refuzata (fail closed).</summary>
[Module("Core")]
public sealed record Undeclared : IQuery<int>;

public sealed class UndeclaredHandler : IQueryHandler<Undeclared, int>
{
    public Task<int> Handle(Undeclared request, CancellationToken cancellationToken) => Task.FromResult(1);
}

/// <summary>Numara apelurile handler-elor de test (scoped).</summary>
public sealed class HandlerCalls
{
    public int Count { get; set; }
}

public sealed class FakeContext : ICurrentContext, Erp.Core.Application.Me.ICurrentUserProfile
{
    public bool IsAuthenticated { get; set; } = true;

    public int UserId { get; set; } = 1;

    public int CompanyId { get; set; } = 1;

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public string? IdempotencyKey { get; set; }

    public string? IpAddress { get; set; } = "127.0.0.1";

    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal) { "Core.Settings.Manage", "Core.Partner.View" };

    public bool HasPermission(string permission, DimensionRef? scope = null) => IsAuthenticated && Permissions.Contains(permission);

    public string UserName => "test";

    public string DisplayName => "Utilizator test";

    public string Language => "ro";

    IReadOnlyCollection<string> Erp.Core.Application.Me.ICurrentUserProfile.Permissions => Permissions;
}

public sealed class FakeLicense : ILicenseInfo
{
    public HashSet<string> Modules { get; } = new(StringComparer.Ordinal) { "Core" };

    public bool HasLicense { get; set; } = true;

    public DateOnly? ValidUntil { get; set; } = new(2099, 12, 31);

    public DateOnly? UpdatesUntil { get; set; }

    public bool ClockBehind { get; set; }

    public HashSet<int> UnlicensedCompanies { get; } = [];

    public bool IsCompanyLicensed(int companyId) => !UnlicensedCompanies.Contains(companyId);

    public string? CustomerName { get; set; } = "Grupul Exemplu";

    public LicenseState State { get; set; } = LicenseState.Valid;

    public bool IsModuleActive(string moduleCode) => Modules.Contains(moduleCode);

    public IReadOnlyCollection<string> ActiveModules => Modules;

    public HashSet<string> FiscalCodes { get; } = new(StringComparer.Ordinal);

    public bool IsFiscalCodeLicensed(string fiscalCode) => FiscalCodes.Count == 0 || FiscalCodes.Contains(fiscalCode);

    public int? GetLimit(string limitCode) => null;
}

public sealed class FakePeriods : IPeriodLock
{
    public HashSet<DateOnly> ClosedMonths { get; } = [];

    public Task<bool> IsClosedAsync(int companyId, DateOnly date, CancellationToken ct) =>
        Task.FromResult(ClosedMonths.Contains(new DateOnly(date.Year, date.Month, 1)));
}

/// <summary>Store-ul structurii pentru testele fara baza: doar firmele utilizatorului (antetul /me).</summary>
public sealed class FakeOrganizationStore : Erp.Core.Application.Organization.IOrganizationStore
{
    public List<Erp.Core.Application.Organization.CompanySummaryDto> Companies { get; } = [];

    public Task<IReadOnlyList<Erp.Core.Application.Organization.CompanySummaryDto>> GetUserCompaniesAsync(int userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Erp.Core.Application.Organization.CompanySummaryDto>>(Companies);

    public Task<Erp.Core.Application.Organization.OrgTreeDto> GetTreeAsync(int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.CompanyDetailDto?> GetCompanyAsync(int companyId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.OrgLookupsDto> GetLookupsAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveGroupAsync(Erp.Core.Application.Organization.SaveCompanyGroup command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveCompanyAsync(Erp.Core.Application.Organization.SaveCompany command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveBranchAsync(Erp.Core.Application.Organization.SaveBranch command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveLocationAsync(Erp.Core.Application.Organization.SaveLocation command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveBankAccountAsync(Erp.Core.Application.Organization.SaveCompanyBankAccount command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveContactAsync(Erp.Core.Application.Organization.SaveCompanyContact command, int userId, CancellationToken ct) => throw new NotSupportedException();
    public Task<Erp.Core.Application.Organization.SavedEntity> SaveAddressAsync(Erp.Core.Application.Organization.SaveCompanyAddress command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveAddressTypeAsync(Erp.Core.Application.Organization.SaveAddressType command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<Erp.Core.Application.Organization.NumberSeriesDto>> ListNumberSeriesAsync(int companyId, short? year, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveNumberSeriesAsync(Erp.Core.Application.NumberSeries.SaveNumberSeries command, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> CloseNumberSeriesAsync(Erp.Core.Application.NumberSeries.CloseNumberSeries command, int userId, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Comanda permisa si in modul doar-citire (de exemplu incarcarea licentei, 08, 4.4).</summary>
[Module("Core")]
[RequiresPermission("Core.Settings.Manage")]
[AllowedWhenLicenseReadOnly]
public sealed record UnlockCommand : ICommand<int>;

public sealed class UnlockCommandHandler : ICommandHandler<UnlockCommand, int>
{
    public Task<int> Handle(UnlockCommand request, CancellationToken cancellationToken) => Task.FromResult(11);
}

/// <summary>Licenta activa, istoricul si cererea de reincarcare, fara baza (08, 4.1).</summary>
public sealed class FakeLicenseFeature : Erp.Core.Application.Licensing.ILicenseSnapshot, Erp.Core.Application.Licensing.ILicenseStore,
    Erp.Core.Application.Licensing.ILicenseRefresh
{
    public Erp.Licensing.License? Current { get; set; }

    public byte KeyId { get; set; }

    public DateOnly ReferenceDate { get; set; } = new(2026, 11, 20);

    public List<(byte[] File, Erp.Licensing.License License)> Saved { get; } = [];

    public int RefreshRequests { get; private set; }

    public Task<int> SaveAsync(byte[] file, Erp.Licensing.License license, byte keyId, int userId, CancellationToken ct)
    {
        Saved.Add((file, license));
        return Task.FromResult(Saved.Count);
    }

    public Task<IReadOnlyList<Erp.Core.Application.Licensing.LicenseHistoryItem>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Erp.Core.Application.Licensing.LicenseHistoryItem>>([]);

    public void RequestRefresh() => RefreshRequests++;
}

/// <summary>Store-ul partenerilor pentru testele fara baza: pipeline-ul il cere la validarea containerului, nu il apeleaza.</summary>
public sealed class FakePartnerStore : Erp.Core.Application.Partners.IPartnerStore
{
    public Task<Erp.Core.Application.Partners.PartnerPageDto> SearchAsync(Erp.Core.Application.Partners.PartnerSearchCriteria criteria, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Erp.Core.Application.Partners.PartnerDetailDto?> GetAsync(int id, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<Erp.Core.Application.Partners.PartnerExportRow>> ExportAsync(Erp.Core.Application.Partners.PartnerSearchCriteria criteria,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Partners.PartnerLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<Erp.Core.Application.Partners.PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<byte[]?> GetPersonalIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();

    public Task<bool> EnsureCodeSeriesAsync(int companyId, short year, int userId, CancellationToken ct) => throw new NotSupportedException();

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveAsync(Erp.Core.Application.Partners.PartnerRecord partner, int userId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveAddressAsync(Erp.Core.Application.Partners.SavePartnerAddress command, int userId,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveBankAccountAsync(Erp.Core.Application.Partners.SavePartnerBankAccount command, int userId,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveContactAsync(Erp.Core.Application.Partners.SavePartnerContact command, int userId,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<Erp.Core.Application.Organization.SavedEntity> SaveGroupAsync(Erp.Core.Application.Partners.SavePartnerGroup command, int userId,
        CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Serviciile partenerilor pentru containerele de test fara baza (ValidateOnBuild).</summary>
public static class FakePartnerServices
{
    public static IServiceCollection AddFakePartnerServices(this IServiceCollection services)
    {
        services.AddSingleton<Erp.Core.Application.Partners.IPartnerStore>(new FakePartnerStore());
        services.AddSingleton<INumberSeriesService>(new FakeNumberSeries());
        services.AddSingleton<Erp.Core.Application.Settings.ISettingsStore>(new FakeSettingsStore());
        services.AddSingleton<Erp.Core.Application.Partners.IPartnerSpreadsheet>(new Erp.Core.Infrastructure.Partners.PartnerSpreadsheet());
        services.AddSingleton<Erp.Core.Application.Properties.IPropertyDefinitionStore>(new FakePropertyStore());
        services.AddSingleton<Erp.Core.Application.Partners.IImportJobStore>(_ => throw new NotSupportedException());
        services.AddSingleton<Erp.Core.Application.Items.IItemStore>(_ => throw new NotSupportedException());
        services.AddSingleton<Erp.Core.Application.Anaf.IAnafVatRegistry>(new FakeAnafRegistry());
        services.AddSingleton<Erp.Core.Application.Partners.IPersonalDataProtector>(
            new Erp.Core.Api.PersonalDataProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        return services;
    }

    private sealed class FakeSettingsStore : Erp.Core.Application.Settings.ISettingsStore
    {
        public Task<Erp.Core.Application.Settings.SettingValue> GetInstallationAsync(string key, CancellationToken ct) => throw new NotSupportedException();

        public Task<byte[]> SaveInstallationAsync(string key, string? value, byte[]? rowVersion, int userId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakePropertyStore : Erp.Core.Application.Properties.IPropertyDefinitionStore
    {
        public Task<IReadOnlyList<Erp.Core.Application.Properties.PropertyDefinitionAdminDto>> ListAsync(string entityType, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Erp.Core.Application.Organization.SavedEntity> SaveAsync(Erp.Core.Application.Properties.SavePropertyDefinition command, int userId,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeAnafRegistry : Erp.Core.Application.Anaf.IAnafVatRegistry
    {
        public Task<Erp.Core.Application.Anaf.AnafCompanyDto?> LookupAsync(string fiscalCode, DateOnly date, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, Erp.Core.Application.Anaf.AnafCompanyDto>> LookupManyAsync(IReadOnlyCollection<string> fiscalCodes,
            DateOnly date, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeNumberSeries : INumberSeriesService
    {
        public Task<DocumentNumber> NextAsync(int companyId, string documentType, DateOnly documentDate, CancellationToken ct, int? branchId = null) =>
            throw new NotSupportedException();
    }
}
