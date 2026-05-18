using Identifier.DomainService.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Identifier.DomainService.Shared
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void AddApplicationServices(this IServiceCollection services)
        {

            // Register services
            services.AddSingleton<IProjectManagementService, ProjectManagementService>();
            services.AddSingleton<IProjectRepository, ProjectRepository>();

            // Drivers
        }
    }
}
