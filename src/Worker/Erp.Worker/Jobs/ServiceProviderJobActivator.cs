using Hangfire;

namespace Erp.Worker.Jobs;

/// <summary>Joburile Hangfire se creeaza din containerul de servicii al Worker-ului, cate un scope pe executie.</summary>
internal sealed class ServiceProviderJobActivator(IServiceScopeFactory scopes) : JobActivator
{
    public override JobActivatorScope BeginScope(JobActivatorContext context) => new Scope(scopes.CreateScope());

    private sealed class Scope(IServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);

        public override void DisposeScope() => scope.Dispose();
    }
}
