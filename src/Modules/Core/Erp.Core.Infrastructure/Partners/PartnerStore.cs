using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Partners;

/// <summary>Partenerii prin procedurile core.usp_Partner* (NET-06), pe conexiunea si tranzactia cererii.</summary>
public sealed class PartnerStore(IDbSession session) : IPartnerStore
{
    public async Task<PartnerPageDto> SearchAsync(PartnerSearchCriteria criteria, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_Partner_Search", new
        {
            criteria.Search, criteria.FiscalSearch, criteria.Role, criteria.PartnerType, criteria.IsActive, criteria.AnafInactive,
            criteria.PropertyDefinitionId, criteria.PropertyOptionId, criteria.PropertyBool, criteria.Offset, criteria.PageSize,
        }, ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<SearchRow>().ConfigureAwait(false)).ToList();
        var active = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        var items = rows.Select(r => new PartnerListItemDto(r.Id, r.Code, r.Name, r.PartnerType, r.FiscalCode, r.IsCustomer, r.IsSupplier, r.VatPayer,
            r.VatOnCollection, r.AnafInactive, r.CountryCode, r.City, r.IsActive)).ToList();
        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        if (rows.Count == 0 && criteria.Offset > 0)
        {
            // Pagina de dupa ultima: totalul se afla cu o cerere de la inceput.
            var first = await SearchAsync(criteria with { Offset = 0, PageSize = 1 }, ct).ConfigureAwait(false);
            total = first.Total;
        }

        return new PartnerPageDto(items, total, active);
    }

    public async Task<PartnerDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_Partner_Get", new { Id = id }, ct)).ConfigureAwait(false);
        var row = await grid.ReadSingleOrDefaultAsync<PartnerRow>().ConfigureAwait(false);
        var addresses = (await grid.ReadAsync<PartnerAddressDto>().ConfigureAwait(false)).ToList();
        var accounts = (await grid.ReadAsync<PartnerBankAccountDto>().ConfigureAwait(false)).ToList();
        var contacts = (await grid.ReadAsync<PartnerContactDto>().ConfigureAwait(false)).ToList();
        var properties = (await grid.ReadAsync<ValueRow>().ConfigureAwait(false))
            .Select(v => new PartnerPropertyValue(v.PropertyDefinitionId, v.ValueText,
                v.ValueNumber?.ToString("0.######", CultureInfo.InvariantCulture),
                v.ValueDate is { } date ? DateOnly.FromDateTime(date) : null, v.ValueBool, v.ValueOptionId, v.ValueUserId))
            .ToList();
        return row is null ? null : new PartnerDetailDto(row.Id, row.Code, row.Name, row.PartnerType, row.FiscalCode, row.HasPersonalId, row.IsCustomer,
            row.IsSupplier, row.PartnerGroupId, row.VatPayer, row.VatOnCollection, row.AnafInactive, Utc(row.AnafCheckedAt), row.TradeRegisterNo,
            row.Address, row.City, row.CountyCode, row.CountryCode, row.PostalCode, row.Phone, row.Email, row.IntraGroupCompanyId, row.IsActive,
            DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc), row.ModifiedByName, DateTime.SpecifyKind(row.LastChangedAt, DateTimeKind.Utc),
            row.RowVersion, addresses, accounts, contacts, properties);
    }

    public async Task<PartnerLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_PartnerLookups_Get", new { CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var groups = (await grid.ReadAsync<PartnerGroupDto>().ConfigureAwait(false)).ToList();
        var properties = (await grid.ReadAsync<PropertyDefinitionDto>().ConfigureAwait(false)).ToList();
        var options = (await grid.ReadAsync<PropertyOptionDto>().ConfigureAwait(false)).ToList();
        var users = (await grid.ReadAsync<UserOptionDto>().ConfigureAwait(false)).ToList();
        var companies = (await grid.ReadAsync<CompanySummaryDto>().ConfigureAwait(false)).ToList();
        return new PartnerLookupsDto(groups, properties, options, users, companies);
    }

    public async Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct) =>
        [.. (await session.Connection.QueryAsync<HistoryRow>(Command("core.usp_Partner_History", new { Id = id }, ct)).ConfigureAwait(false))
            .Select(h => new PartnerHistoryEntryDto(h.Id, DateTime.SpecifyKind(h.OccurredAt, DateTimeKind.Utc), h.Action, h.EntityType, h.EntityId,
                h.UserName, h.Changes))];

    public Task<byte[]?> GetPersonalIdAsync(int id, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]?>(Command("core.usp_Partner_GetPersonalId", new { Id = id }, ct));

    public Task<SavedEntity> SaveAsync(PartnerRecord partner, int userId, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new
        {
            partner.Id, partner.Code, partner.Name, partner.PartnerType, partner.FiscalCode, PersonalIdChange = partner.PersonalIdChange,
            partner.IsCustomer, partner.IsSupplier, partner.PartnerGroupId, partner.VatPayer, partner.VatOnCollection,
            partner.AnafInactive, partner.TradeRegisterNo, partner.Address, partner.City, partner.CountyCode, CountryCode = Ansi(partner.CountryCode, 2),
            partner.PostalCode, partner.Phone, partner.Email, partner.IntraGroupCompanyId, partner.IsActive,
            SaveProperties = partner.Properties is not null, partner.RowVersion, partner.AnafCheckedAt, UserId = userId,
        });
        parameters.Add("PersonalIdEncrypted", partner.PersonalIdEncrypted, DbType.Binary, size: 256);
        parameters.Add("Properties", PropertyTable(partner.Properties ?? []).AsTableValuedParameter("core.PropertyValueList"));
        return session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_Partner_Save", parameters, ct));
    }

    public Task<bool> EnsureCodeSeriesAsync(int companyId, short year, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<bool>(Command("core.usp_NumberSeries_EnsureDefault", new
        {
            CompanyId = companyId, DocumentTypeCode = PartnerCodeSeries.DocumentType, Year = year, Prefix = PartnerCodeSeries.DefaultPrefix,
            Padding = PartnerCodeSeries.DefaultPadding, UserId = userId,
        }, ct));

    public Task<SavedEntity> SaveAddressAsync(SavePartnerAddress command, int userId, CancellationToken ct) => Save("core.usp_PartnerAddress_Save", new
    {
        command.Id, command.PartnerId, command.AddressType, command.Name, command.Address, command.City, command.CountyCode,
        CountryCode = Ansi(command.CountryCode, 2), command.PostalCode, command.Phone, command.Email, command.IsDefault, command.IsActive,
        command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveBankAccountAsync(SavePartnerBankAccount command, int userId, CancellationToken ct) => Save("core.usp_PartnerBankAccount_Save", new
    {
        command.Id, command.PartnerId, Iban = Ansi(command.Iban, 34), command.BankName, Currency = Ansi(command.Currency, 3), command.IsDefault,
        command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveContactAsync(SavePartnerContact command, int userId, CancellationToken ct) => Save("core.usp_PartnerContact_Save", new
    {
        command.Id, command.PartnerId, command.Name, command.Role, command.Phone, command.Email, command.Notes, command.IsActive, command.RowVersion,
        UserId = userId,
    }, ct);

    public Task<SavedEntity> SaveGroupAsync(SavePartnerGroup command, int userId, CancellationToken ct) => Save("core.usp_PartnerGroup_Save", new
    {
        command.Id, command.Code, command.Name, command.SortOrder, command.IsActive, command.RowVersion, UserId = userId,
    }, ct);

    private static DataTable PropertyTable(IReadOnlyList<PartnerPropertyValue> values)
    {
        var table = new DataTable();
        table.Columns.Add("PropertyDefinitionId", typeof(int));
        table.Columns.Add("ValueText", typeof(string));
        table.Columns.Add("ValueNumber", typeof(decimal));
        table.Columns.Add("ValueDate", typeof(DateTime));
        table.Columns.Add("ValueBool", typeof(bool));
        table.Columns.Add("ValueOptionId", typeof(int));
        table.Columns.Add("ValueUserId", typeof(int));
        foreach (var v in values)
        {
            table.Rows.Add(v.PropertyDefinitionId, (object?)v.ValueText?.Trim() ?? DBNull.Value,
                v.ValueNumber is null ? DBNull.Value : decimal.Parse(v.ValueNumber, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                v.ValueDate is { } date ? date.ToDateTime(TimeOnly.MinValue) : DBNull.Value,
                (object?)v.ValueBool ?? DBNull.Value, (object?)v.ValueOptionId ?? DBNull.Value, (object?)v.ValueUserId ?? DBNull.Value);
        }

        return table;
    }

    private Task<SavedEntity> Save(string procedure, object parameters, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command(procedure, parameters, ct));

    private CommandDefinition Command(string procedure, object? parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DbString Ansi(string value, int length) => new() { Value = value, Length = length, IsAnsi = true, IsFixedLength = length <= 3 };

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private sealed record SearchRow(int Id, string Code, string Name, byte PartnerType, string? FiscalCode, bool IsCustomer, bool IsSupplier, bool VatPayer,
        bool VatOnCollection, bool AnafInactive, string CountryCode, string City, bool IsActive, int TotalCount);

    private sealed record PartnerRow(int Id, string Code, string Name, byte PartnerType, string? FiscalCode, bool HasPersonalId, bool IsCustomer,
        bool IsSupplier, int? PartnerGroupId, bool VatPayer, bool VatOnCollection, bool AnafInactive, DateTime? AnafCheckedAt, string? TradeRegisterNo,
        string Address, string City, string? CountyCode, string CountryCode, string? PostalCode, string? Phone, string? Email, int? IntraGroupCompanyId,
        bool IsActive, DateTime CreatedAt, string ModifiedByName, DateTime LastChangedAt, byte[] RowVersion);

    private sealed record ValueRow(int PropertyDefinitionId, string? ValueText, decimal? ValueNumber, DateTime? ValueDate, bool? ValueBool,
        int? ValueOptionId, int? ValueUserId);

    private sealed record HistoryRow(long Id, DateTime OccurredAt, string Action, string EntityType, long? EntityId, string UserName, string? Changes);
}
