using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BlocksTemplate.DomainService;

public static class ServiceRegistry
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IEventService, EventService>();
        services.AddValidatorsFromAssembly(typeof(IEventService).Assembly);
        return services;
    }
}
