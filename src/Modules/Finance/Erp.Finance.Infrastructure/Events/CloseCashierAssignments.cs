using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;

namespace Erp.Finance.Infrastructure.Events;

/// <summary>
/// Consumatorul UtilizatorDezactivat (02.02, F1 5.2): inchide atribuirile la case ale utilizatorului dezactivat cu ziua dezactivarii (ora
/// Romaniei), in tranzactia consumatorului (02.01, 4.5).
/// </summary>
public sealed class CloseCashierAssignments(IDbSession session, TimeProvider time) : IIntegrationEventHandler<UtilizatorDezactivat>
{
    public Task HandleAsync(UtilizatorDezactivat evt, CancellationToken ct) =>
        session.Connection.ExecuteAsync(new CommandDefinition("fin.usp_TreasuryAccountUser_CloseForUser",
            new { evt.UserId, Day = BusinessDay.Today(time).ToDateTime(TimeOnly.MinValue) }, session.Transaction, commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
}
