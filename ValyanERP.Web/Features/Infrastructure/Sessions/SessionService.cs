using System;
using System.Threading.Tasks;

namespace ValyanERP.Web.Features.Infrastructure.Sessions;

public class SessionService : ISessionService
{
    private readonly ISessionsRepository _repo;

    public SessionService(ISessionsRepository repo)
    {
        _repo = repo;
    }

    public async Task<Session> CreateSessionAsync(Guid userId, string? circuitId = null, TimeSpan? ttl = null)
    {
        var token = Guid.NewGuid();
var now = ValyanERP.Web.Infrastructure.Time.TimeUtils.NowRomania();
            var session = new Session
        {
            UserId = userId,
            Token = token,
            CircuitId = circuitId,
            IsActive = true,
            CreatedAt = now,
            LastHeartbeat = now,
            ExpiresAt = ttl.HasValue ? now.Add(ttl.Value) : (DateTime?)null
        };

        await _repo.CreateAsync(session);
        return session;
    }

    public async Task<Session?> ValidateTokenAsync(Guid token)
    {
        var s = await _repo.GetByTokenAsync(token);
        if (s == null) return null;
        if (!s.IsActive) return null;
        
        // Use consistent timezone for comparison
        var now = ValyanERP.Web.Infrastructure.Time.TimeUtils.NowRomania();
        if (s.ExpiresAt.HasValue && s.ExpiresAt.Value < now) 
            return null;
            
        return s;
    }

    public Task InvalidateByTokenAsync(Guid token) => _repo.InvalidateAsync(token);
    public Task InvalidateByCircuitIdAsync(string circuitId) => _repo.InvalidateByCircuitIdAsync(circuitId);
    public Task InvalidateByUserIdAsync(Guid userId) => _repo.InvalidateByUserIdAsync(userId);

    public Task RefreshHeartbeatAsync(Guid token) => _repo.UpdateHeartbeatAsync(token);
}