using Flowlio.Application.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace Flowlio.Application;

public static class ApplicationModule
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<FlowlioMapper>();
        return services;
    }
}
