using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Erp.Core.Application.Licensing;
using Erp.Core.Contracts;
using Erp.Licensing;

namespace Erp.Core.Infrastructure.Licensing;

/// <summary>Licenta in baza (08, 4.1), prin procedurile core.usp_InstallationLicense_*, pe conexiunea si tranzactia cererii.</summary>
public sealed class LicenseStore(IDbSession session) : ILicenseStore
{
    public async Task<int> SaveAsync(byte[] file, License license, byte keyId, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<SavedRow>(new CommandDefinition("core.usp_InstallationLicense_Save", new
        {
            LicenseFile = file,
            license.LicenseId,
            KeyId = keyId.ToString(CultureInfo.InvariantCulture),
            license.CustomerName,
            FiscalCodes = JsonSerializer.Serialize(license.FiscalCodeList),
            Modules = JsonSerializer.Serialize(license.Modules),
            LimitCompanies = license.Companies,
            LimitUsers = license.Limits.Users,
            LimitPayrollEmployees = license.Limits.PayrollEmployees,
            LimitPosTerminals = license.Limits.PosTerminals,
            IssuedDate = license.IssuedAt.ToDateTime(TimeOnly.MinValue),
            ValidUntil = license.ValidUntil?.ToDateTime(TimeOnly.MinValue),
            UpdatesUntil = license.UpdatesUntil?.ToDateTime(TimeOnly.MinValue),
            UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        return row.Id;
    }

    public async Task<IReadOnlyList<LicenseHistoryItem>> ListAsync(CancellationToken ct)
    {
        var rows = await session.Connection.QueryAsync<HistoryRow>(new CommandDefinition("core.usp_InstallationLicense_List", null,
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new LicenseHistoryItem(r.LicenseId, r.KeyId, r.CustomerName, r.LimitCompanies, DateOnly.FromDateTime(r.IssuedDate),
            r.ValidUntil is { } v ? DateOnly.FromDateTime(v) : null, r.UpdatesUntil is { } u ? DateOnly.FromDateTime(u) : null, r.IsCurrent,
            DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)))];
    }

    private sealed record SavedRow(int Id, string LicenseId, DateTime? ValidUntil);

    private sealed record HistoryRow(int Id, string LicenseId, string KeyId, string CustomerName, int LimitCompanies, DateTime IssuedDate,
        DateTime? ValidUntil, DateTime? UpdatesUntil, bool IsCurrent, DateTime CreatedAt);
}
