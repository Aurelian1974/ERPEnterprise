using System;
using System.Threading.Tasks;

namespace ValyanERP.Web.Features.Infrastructure.Sessions;

public interface ISessionService
{
    Task<Session> CreateSessionAsync(Guid userId, string? circuitId = null, TimeSpan? ttl = null);
    Task<Session?> ValidateTokenAsync(Guid token);
    Task InvalidateByTokenAsync(Guid token);
    Task InvalidateByCircuitIdAsync(string circuitId);
    Task InvalidateByUserIdAsync(Guid userId);
    Task RefreshHeartbeatAsync(Guid token);
}