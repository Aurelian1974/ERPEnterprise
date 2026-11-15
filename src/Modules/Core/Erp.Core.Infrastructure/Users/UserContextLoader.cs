using System.Data;
using Dapper;
using Erp.Core.Application.Abstractions;
using Erp.Core.Infrastructure.Data;

namespace Erp.Core.Infrastructure.Users;

/// <summary>core.usp_User_GetPermissions: utilizatorul activ si permisiunile lui pe firma ceruta.</summary>
public sealed class UserContextLoader(DbSession session) : IUserContextLoader
{
    public async Task<UserContextData?> LoadAsync(Guid subjectId, int? companyId, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct).ConfigureAwait(false);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition("core.usp_User_GetPermissions",
            new { SubjectId = subjectId, CompanyId = companyId }, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var user = await grid.ReadSingleOrDefaultAsync<UserRow>().ConfigureAwait(false);
        var permissions = (await grid.ReadAsync<string>().ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        return user is null
            ? null
            : new UserContextData(user.Id, user.UserName, user.DisplayName, user.Language, user.CompanyId, permissions);
    }

    private sealed record UserRow(int Id, string UserName, string DisplayName, string? Email, string Language, int CompanyId, bool IsSystem, bool IsEmployee);
}
