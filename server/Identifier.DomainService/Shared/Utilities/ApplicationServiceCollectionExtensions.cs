using Blocks.Extension.DependencyInjection;
using Identifier.DomainService.Projects;
using Identifier.DomainService.Shared.Utilities;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Storage.DomainService.Shared.Services;
using DomainService.Storage;
using DomainService.Storage.Validators;
using Storage.DomainService.Storage;
using Storage.DomainService.Storage.Validators;

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
            services.AddSingleton<DmsArtifactBuilderFactory>();
            services.AddTransient<IValidator<UpdateFileRequest>, UpdateFileRequestValidator>(); 
            services.AddTransient<AwsS3CompatibleStorageService>();
            services.AddSingleton<FileArtifactBuilder>();
            services.AddSingleton<FolderArtifactBuilder>();

            services.RegisterBlocksStorageServices();
            services.RegisterBlocksMailService();
        }
    }
}
