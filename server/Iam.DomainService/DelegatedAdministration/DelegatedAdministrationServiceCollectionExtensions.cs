using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Iam.DomainService.DelegatedAdministration
{
    public static class DelegatedAdministrationServiceCollectionExtensions
    {
        /// <summary>
        /// Registers delegated administration and binds <c>DelegatedAdministration:Mode</c>. Safe to
        /// call from both registration roots: everything is TryAdd.
        /// </summary>
        public static IServiceCollection AddDelegatedAdministration(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOptions<DelegatedAdministrationOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                    configuration.GetSection(DelegatedAdministrationOptions.SectionName).Bind(options));
            services.TryAddSingleton<IDelegatedAdministrationRepository, DelegatedAdministrationRepository>();
            services.TryAddSingleton<IDelegatedAdministrationService, DelegatedAdministrationService>();

            return services;
        }
    }
}
