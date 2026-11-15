using Erp.Core.Application.Me;
using Erp.Core.Contracts;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Api;

public static class CoreApiRegistration
{
    /// <summary>Contextul cererii si ProblemDetails, comune tuturor modulelor.</summary>
    public static IServiceCollection AddErpApi(this IServiceCollection services)
    {
        services.AddScoped<HttpCurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<HttpCurrentContext>());
        services.AddScoped<ICurrentUserProfile>(sp => sp.GetRequiredService<HttpCurrentContext>());
        services.AddProblemDetails();
        services.AddExceptionHandler<ErpExceptionHandler>();
        return services;
    }

    /// <summary>Tratarea erorilor si contextul cererii; dupa autentificare, inainte de endpoint-uri.</summary>
    public static IApplicationBuilder UseErpApi(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<CurrentContextMiddleware>();
        return app;
    }

    /// <summary>Endpoint-urile Nucleului, subtiri (NET-03): cererea merge direct in MediatR.</summary>
    public static IEndpointRouteBuilder MapCoreApi(this IEndpointRouteBuilder endpoints)
    {
        var core = endpoints.MapGroup("/api/core").WithTags("Core");
        core.MapGet("/me", (ISender sender, CancellationToken ct) => sender.Send(new GetCurrentUser(), ct))
            .WithName("GetCurrentUser")
            .Produces<CurrentUserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return endpoints;
    }
}
