using Microsoft.Extensions.DependencyInjection;
using Shared.Kernel.Abstractions;

namespace Shared.Infrastructure.MultiTenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public string TenantName { get; private set; } = string.Empty;

    public void Set(Guid tenantId, string tenantName)
    {
        TenantId = tenantId;
        TenantName = tenantName;
    }
}
