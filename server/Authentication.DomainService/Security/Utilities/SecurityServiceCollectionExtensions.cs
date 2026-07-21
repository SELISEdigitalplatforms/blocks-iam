using Authentication.DomainService.Security.Repositories;
using Authentication.DomainService.Security.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Authentication.DomainService.Security.Utilities
{
    public static class SecurityServiceCollectionExtensions
    {
        public static void RegisterSecurityServices(this IServiceCollection services)
        {
            services.AddSingleton<ISecurityRepository, SecurityRepository>();
            services.AddSingleton<ISecurityQueryService, SecurityQueryService>();
            services.AddSingleton<IActivityQueryService, ActivityQueryService>();
            services.AddSingleton<ISessionRevocationService, SessionRevocationService>();
            services.AddHttpContextAccessor();
        }
    }
}
