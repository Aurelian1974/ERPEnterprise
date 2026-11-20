using System.Data;
using Dapper;
using Erp.Core.Application.Settings;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Data;

/// <summary>Setarile instalarii prin core.usp_Setting_Get / _Save (NET-06).</summary>
public sealed class SettingsStore(IDbSession session) : ISettingsStore
{
    private const string Installation = "Installation";

    public async Task<SettingValue> GetInstallationAsync(string key, CancellationToken ct) =>
        await session.Connection.QuerySingleOrDefaultAsync<SettingValue>(new CommandDefinition("core.usp_Setting_Get",
            new { Scope = Ansi(Installation, 20), ScopeId = (int?)null, Key = Ansi(key, 100) }, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)
        ?? new SettingValue(null, null);

    public Task<byte[]> SaveInstallationAsync(string key, string? value, byte[]? rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(new CommandDefinition("core.usp_Setting_Save",
            new { Scope = Ansi(Installation, 20), ScopeId = (int?)null, Key = Ansi(key, 100), Value = value, RowVersion = rowVersion, UserId = userId },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private static DbString Ansi(string value, int length) => new() { Value = value, Length = length, IsAnsi = true };
}
