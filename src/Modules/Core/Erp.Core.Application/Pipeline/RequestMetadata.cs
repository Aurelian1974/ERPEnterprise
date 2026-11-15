using System.Reflection;
using Erp.Core.Contracts;

namespace Erp.Core.Application.Pipeline;

/// <summary>Metadatele declarate pe comenzi si interogari (NET-04), citite o singura data per tip.</summary>
internal static class RequestMetadata<TRequest>
{
    public static readonly bool IsCommand = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    public static readonly string? Module = typeof(TRequest).GetCustomAttribute<ModuleAttribute>()?.ModuleCode;

    public static readonly string? Permission = typeof(TRequest).GetCustomAttribute<RequiresPermissionAttribute>()?.Permission;

    public static readonly bool AnyAuthenticatedUser = typeof(TRequest).GetCustomAttribute<AuthenticatedUserAttribute>() is not null;

    public static readonly string? AuditAction = typeof(TRequest).GetCustomAttribute<AuditedAttribute>()?.Action;

    public static readonly string Name = typeof(TRequest).FullName ?? typeof(TRequest).Name;
}
