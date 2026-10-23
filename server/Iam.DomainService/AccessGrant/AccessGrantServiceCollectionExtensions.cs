using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Iam.DomainService.AccessGrant
{
    public static class AccessGrantServiceCollectionExtensions
    {
        /// <summary>
        /// Registers access grant. Safe to
        /// call from both registration roots: everything is TryAdd.
        /// </summary>
        public static IServiceCollection AddAccessGrant(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<IAccessGrantRepository, AccessGrantRepository>();
            services.TryAddSingleton<IAccessGrantService, AccessGrantService>();

            return services;
        }
    }
}
