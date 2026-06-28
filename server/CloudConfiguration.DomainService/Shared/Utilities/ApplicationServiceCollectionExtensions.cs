using Microsoft.Extensions.DependencyInjection;
using CloudConfiguration.DomainService.Shared.Services;

namespace CloudConfiguration.DomainService.Shared.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void AddCloudConfigurationServices(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IConfigurationService, ConfigurationService>();
            serviceCollection.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
        }
    }
}