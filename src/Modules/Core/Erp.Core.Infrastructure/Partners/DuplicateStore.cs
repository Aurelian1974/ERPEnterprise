using System.Data;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Partners;

/// <summary>Dublurile partenerilor si articolelor (core.usp_Duplicate_Find, usp_DuplicateIgnore_Save, usp_Partner_Merge, usp_Item_Merge).</summary>
public sealed class DuplicateStore(IDbSession session) : IDuplicateStore
{
    public async Task<IReadOnlyList<DuplicatePairDto>> FindAsync(byte entityType, CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<DuplicatePairDto>(Command("core.usp_Duplicate_Find", new { EntityType = entityType }, ct)).ConfigureAwait(false)];

    public Task IgnoreAsync(byte entityType, int firstId, int secondId, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("core.usp_DuplicateIgnore_Save",
            new { EntityType = entityType, FirstId = firstId, SecondId = secondId, UserId = userId }, ct));

    public Task<SavedEntity> MergeAsync(byte entityType, int keepId, int removeId, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command(entityType == DuplicateEntity.Item ? "core.usp_Item_Merge" : "core.usp_Partner_Merge",
            new { KeepId = keepId, RemoveId = removeId, UserId = userId }, ct));

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
