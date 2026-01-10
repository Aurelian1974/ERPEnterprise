using Dapper;
using ValyanERP.Web.Infrastructure.Data;
using System.Data;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace ValyanERP.Web.Features.Infrastructure.Sessions;

public class SessionsRepository : ISessionsRepository
{
    private readonly DapperContext _context;

    public SessionsRepository(DapperContext context)
    {
        _context = context;
    }

    public async Task<Guid> CreateAsync(Session session)
    {
        const string sql = @"
INSERT INTO dbo.Sessions (Id, UserId, Token, CircuitId, IsActive, CreatedAt, LastHeartbeat, ExpiresAt, Metadata)
VALUES (@Id, @UserId, @Token, @CircuitId, @IsActive, @CreatedAt, @LastHeartbeat, @ExpiresAt, @Metadata)";
        session.Id = Guid.NewGuid();
        using var conn = _context.CreateConnection();
        await conn.ExecuteAsync(sql, session);
        return session.Id;
    }

    public async Task<Session?> GetByTokenAsync(Guid token)
    {
        using var conn = _context.CreateConnection();
        const string sql = "SELECT * FROM dbo.Sessions WHERE Token = @Token AND IsActive = 1";
        return await conn.QuerySingleOrDefaultAsync<Session>(sql, new { Token = token });
    }

    public async Task<Session?> GetByIdAsync(Guid id)
    {
        using var conn = _context.CreateConnection();
        const string sql = "SELECT * FROM dbo.Sessions WHERE Id = @Id";
        return await conn.QuerySingleOrDefaultAsync<Session>(sql, new { Id = id });
    }

    public async Task<IEnumerable<Session>> GetByUserIdAsync(Guid userId)
    {
        using var conn = _context.CreateConnection();
        const string sql = "SELECT * FROM dbo.Sessions WHERE UserId = @UserId";
        return await conn.QueryAsync<Session>(sql, new { UserId = userId });
    }

    public async Task InvalidateAsync(Guid token)
    {
        using var conn = _context.CreateConnection();
        const string sql = "UPDATE dbo.Sessions SET IsActive = 0 WHERE Token = @Token";
        await conn.ExecuteAsync(sql, new { Token = token });
    }

    public async Task InvalidateByCircuitIdAsync(string circuitId)
    {
        using var conn = _context.CreateConnection();
        const string sql = "UPDATE dbo.Sessions SET IsActive = 0 WHERE CircuitId = @CircuitId";
        await conn.ExecuteAsync(sql, new { CircuitId = circuitId });
    }

    public async Task InvalidateByUserIdAsync(Guid userId)
    {
        using var conn = _context.CreateConnection();
        const string sql = "UPDATE dbo.Sessions SET IsActive = 0 WHERE UserId = @UserId";
        await conn.ExecuteAsync(sql, new { UserId = userId });
    }

    public async Task<int> DeleteExpiredAsync(DateTime threshold)
    {
        using var conn = _context.CreateConnection();
        const string sql = "DELETE FROM dbo.Sessions WHERE IsActive = 0 AND CreatedAt < @Threshold";
        return await conn.ExecuteAsync(sql, new { Threshold = threshold });
    }

    public async Task UpdateHeartbeatAsync(Guid token)
    {
        using var conn = _context.CreateConnection();
        // Use Bucharest timezone for consistency
        var now = ValyanERP.Web.Infrastructure.Time.TimeUtils.NowRomania();
        const string sql = "UPDATE dbo.Sessions SET LastHeartbeat = @Now WHERE Token = @Token AND IsActive = 1";
        await conn.ExecuteAsync(sql, new { Token = token, Now = now });
    }

    public async Task<int> InvalidateStaleSessionsAsync(DateTime staleBefore)
    {
        using var conn = _context.CreateConnection();
        const string sql = "UPDATE dbo.Sessions SET IsActive = 0 WHERE IsActive = 1 AND (LastHeartbeat IS NULL OR LastHeartbeat < @StaleBefore)";
        return await conn.ExecuteAsync(sql, new { StaleBefore = staleBefore });
    }

    // ⚠️ SECURITY: Invalidate all active sessions (called on application shutdown)
    public async Task InvalidateAllSessionsAsync()
    {
        using var conn = _context.CreateConnection();
        const string sql = "UPDATE dbo.Sessions SET IsActive = 0 WHERE IsActive = 1";
        await conn.ExecuteAsync(sql);
    }
}