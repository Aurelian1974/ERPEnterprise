using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ValyanERP.Web.Features.Infrastructure.Sessions;

public interface ISessionsRepository
{
    Task<Guid> CreateAsync(Session session);
    Task<Session?> GetByTokenAsync(Guid token);
    Task<Session?> GetByIdAsync(Guid id);
    Task<IEnumerable<Session>> GetByUserIdAsync(Guid userId);
    Task InvalidateAsync(Guid token);
    Task InvalidateByCircuitIdAsync(string circuitId);
    Task InvalidateByUserIdAsync(Guid userId);
    Task<int> DeleteExpiredAsync(DateTime threshold);
    Task UpdateHeartbeatAsync(Guid token);
    Task<int> InvalidateStaleSessionsAsync(DateTime staleBefore);
    Task InvalidateAllSessionsAsync(); // ⚠️ SECURITY: Invalidate all sessions on app shutdown
}