using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Application.NumberSeries;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using Erp.Core.Domain;

namespace Erp.Core.Infrastructure.Organization;

/// <summary>Structura organizatorica si seriile prin procedurile core.usp_* (NET-06), pe conexiunea si tranzactia cererii.</summary>
public sealed class OrganizationStore(IDbSession session) : IOrganizationStore
{
    public async Task<OrgTreeDto> GetTreeAsync(int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_OrgTree_Get", new { UserId = userId }, ct)).ConfigureAwait(false);
        var groups = (await grid.ReadAsync<CompanyGroupNode>().ConfigureAwait(false)).ToList();
        var companies = (await grid.ReadAsync<CompanyNode>().ConfigureAwait(false)).ToList();
        var branches = (await grid.ReadAsync<BranchNode>().ConfigureAwait(false)).ToList();
        var locations = (await grid.ReadAsync<LocationNode>().ConfigureAwait(false)).ToList();
        var contacts = (await grid.ReadAsync<CompanyContactDto>().ConfigureAwait(false)).ToList();
        return new OrgTreeDto(groups, companies, branches, locations, contacts);
    }

    public async Task<CompanyDetailDto?> GetCompanyAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_Company_Get", new { Id = companyId }, ct)).ConfigureAwait(false);
        var row = await grid.ReadSingleOrDefaultAsync<CompanyRow>().ConfigureAwait(false);
        var accounts = (await grid.ReadAsync<CompanyBankAccountDto>().ConfigureAwait(false)).ToList();
        var addresses = (await grid.ReadAsync<CompanyAddressDto>().ConfigureAwait(false)).ToList();
        var contacts = (await grid.ReadAsync<CompanyContactDto>().ConfigureAwait(false)).ToList();
        return row is null ? null : new CompanyDetailDto(row.Id, row.GroupId, row.Code, row.Name, row.FiscalCode, row.VatPayer, row.VatOnCollection,
            row.TradeRegisterNo, row.ShareCapital?.ToString("0.00", CultureInfo.InvariantCulture), row.Address, row.City, row.CountyCode,
            row.CountryCode, row.PostalCode, row.FunctionalCurrency, row.DefaultPrecisionPolicyId, row.IncludeInConsolidation, row.IsActive,
            row.ModifiedByName, DateTime.SpecifyKind(row.LastChangedAt, DateTimeKind.Utc), row.RowVersion, accounts, addresses, row.Phone, row.Email, contacts);
    }

    public async Task<OrgLookupsDto> GetLookupsAsync(CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_OrgLookups_Get", null, ct)).ConfigureAwait(false);
        var countries = (await grid.ReadAsync<CountryDto>().ConfigureAwait(false)).ToList();
        var counties = (await grid.ReadAsync<CountyDto>().ConfigureAwait(false)).ToList();
        var policies = (await grid.ReadAsync<PrecisionPolicyDto>().ConfigureAwait(false)).ToList();
        var types = (await grid.ReadAsync<DocumentTypeDto>().ConfigureAwait(false)).ToList();
        var addressTypes = (await grid.ReadAsync<AddressTypeDto>().ConfigureAwait(false)).ToList();
        return new OrgLookupsDto(countries, counties, policies, types, addressTypes);
    }

    public async Task<IReadOnlyList<CompanySummaryDto>> GetUserCompaniesAsync(int userId, CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<CompanySummaryDto>(Command("core.usp_User_GetCompanies", new { UserId = userId }, ct)).ConfigureAwait(false)];

    public Task<SavedEntity> SaveGroupAsync(SaveCompanyGroup command, int userId, CancellationToken ct) => SaveAsync("core.usp_CompanyGroup_Save", new
    {
        command.Id, command.Code, command.Name, command.ParentGroupId, command.ParentCompanyId, command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveCompanyAsync(SaveCompany command, int userId, CancellationToken ct) => SaveAsync("core.usp_Company_Save", new
    {
        command.Id, command.GroupId, command.Code, command.Name, command.FiscalCode, command.VatPayer, command.VatOnCollection,
        command.TradeRegisterNo,
        ShareCapital = command.ShareCapital is null ? (decimal?)null : decimal.Parse(command.ShareCapital, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
        command.Address, command.City,         command.CountyCode, CountryCode = Ansi(command.CountryCode, 2), command.PostalCode, command.Phone, command.Email,
        command.DefaultPrecisionPolicyId, command.IncludeInConsolidation, command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveBranchAsync(SaveBranch command, int userId, CancellationToken ct) => SaveAsync("core.usp_Branch_Save", new
    {
        command.Id, command.CompanyId, command.Code, command.Name, command.Address, command.City, command.CountyCode,
        CountryCode = Ansi(command.CountryCode, 2), command.PostalCode, command.Phone, command.Email, command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveLocationAsync(SaveLocation command, int userId, CancellationToken ct) => SaveAsync("core.usp_Location_Save", new
    {
        command.Id, command.CompanyId, command.BranchId, command.Code, command.Name, command.LocationType, command.Address, command.City,
        command.CountyCode, command.CountryCode, command.PostalCode, command.Phone, command.Email, command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveBankAccountAsync(SaveCompanyBankAccount command, int userId, CancellationToken ct) => SaveAsync("core.usp_CompanyBankAccount_Save", new
    {
        command.Id, command.CompanyId, Iban = Ansi(command.Iban, 34), command.BankName, Currency = Ansi(command.Currency, 3), command.IsDefault,
        command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveAddressAsync(SaveCompanyAddress command, int userId, CancellationToken ct) => SaveAsync("core.usp_CompanyAddress_Save", new
    {
        command.Id, command.CompanyId, command.AddressType, command.Name, command.Address, command.City, command.CountyCode,
        CountryCode = Ansi(command.CountryCode, 2),         command.PostalCode, command.Phone, command.Email, command.IsDefault, command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

            public Task<SavedEntity> SaveContactAsync(SaveCompanyContact command, int userId, CancellationToken ct) => SaveAsync("core.usp_CompanyContact_Save", new
            {
                command.Id, command.CompanyId, command.Name, command.Role, command.Phone, command.Email, command.Notes, command.BranchId, command.LocationId,
                command.IsActive, command.RowVersion,
                UserId = userId,
            }, ct);
    public Task<SavedEntity> SaveAddressTypeAsync(SaveAddressType command, int userId, CancellationToken ct) => SaveAsync("core.usp_AddressType_Save", new
    {
        command.Id, command.Code, command.Name, command.SortOrder, command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    public async Task<IReadOnlyList<NumberSeriesDto>> ListNumberSeriesAsync(int companyId, short? year, CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<NumberSeriesDto>(Command("core.usp_NumberSeries_List", new { CompanyId = companyId, Year = year }, ct))
            .ConfigureAwait(false)];

    public Task<SavedEntity> SaveNumberSeriesAsync(SaveNumberSeries command, int userId, CancellationToken ct) => SaveAsync("core.usp_NumberSeries_Save", new
    {
        command.Id, command.CompanyId, command.BranchId, command.DocumentTypeCode, command.Prefix, command.Year, command.NextNumber, command.Padding,
        command.IsFiscal, command.IsDefault, command.AutoRollover, command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> CloseNumberSeriesAsync(CloseNumberSeries command, int userId, CancellationToken ct) => SaveAsync("core.usp_NumberSeries_Close", new
    {
        command.Id, command.CompanyId, command.RowVersion, UserId = userId,
    }, ct);

    private Task<SavedEntity> SaveAsync(string procedure, object parameters, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command(procedure, parameters, ct));

    private CommandDefinition Command(string procedure, object? parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DbString Ansi(string value, int length) => new() { Value = value, Length = length, IsAnsi = true, IsFixedLength = length <= 3 };

    private sealed record CompanyRow(int Id, int? GroupId, string Code, string Name, string FiscalCode, bool VatPayer, bool VatOnCollection,
        string TradeRegisterNo, decimal? ShareCapital, string Address, string City, string? CountyCode, string CountryCode, string? PostalCode,
                string? Phone, string? Email, string FunctionalCurrency, int DefaultPrecisionPolicyId, bool IncludeInConsolidation, bool IsActive, DateTime? ModifiedAt,
        string ModifiedByName, DateTime LastChangedAt, byte[] RowVersion);
}

/// <summary>INumberSeriesService (02.01, 4.2) peste core.usp_NumberSeries_Next, in tranzactia documentului.</summary>
public sealed class NumberSeriesService(IDbSession session) : INumberSeriesService
{
    public async Task<DocumentNumber> NextAsync(int companyId, string documentType, DateOnly documentDate, CancellationToken ct, int? branchId = null)
    {
        if (session.Transaction is null)
        {
            throw new InvalidOperationException("Numărul se alocă doar în tranzacția documentului (4.2).");
        }

        var row = await session.Connection.QuerySingleAsync<AllocatedNumber>(new CommandDefinition("core.usp_NumberSeries_Next",
            new { CompanyId = companyId, BranchId = branchId, DocumentTypeCode = documentType, DocumentDate = documentDate.ToDateTime(TimeOnly.MinValue) },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        return new DocumentNumber(row.Prefix, row.Year, row.Number, DocumentNumberFormat.Format(row.Prefix, row.Year, row.Number, row.Padding));
    }

    private sealed record AllocatedNumber(int SeriesId, string Prefix, short Year, long Number, byte Padding);
}
