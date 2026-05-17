using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Abstractions;

public interface IModuleInstaller
{
    IServiceCollection Install(IServiceCollection services, IConfiguration configuration);
}
