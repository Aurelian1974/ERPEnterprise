using System;

namespace ValyanERP.Web.Features.Infrastructure.Sessions;

public class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid Token { get; set; }
    public string? CircuitId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = ValyanERP.Web.Infrastructure.Time.TimeUtils.NowRomania();
    public DateTime? LastHeartbeat { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Metadata { get; set; }
}