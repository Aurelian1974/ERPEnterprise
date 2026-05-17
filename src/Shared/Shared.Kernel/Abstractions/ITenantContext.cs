namespace Shared.Kernel.Abstractions;

public interface ITenantContext
{
    Guid TenantId { get; }
    string TenantName { get; }
}
