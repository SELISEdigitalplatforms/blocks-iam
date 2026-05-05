using Blocks.Extension.DependencyInjection;
using Identifier.DomainService.Certificate;
using Identifier.DomainService.ManagedService;
using Identifier.DomainService.ManagedService.Services;
using Identifier.DomainService.ManagedService.Validator;
using Identifier.DomainService.Migration;
using Identifier.DomainService.Migration.Services;
using Identifier.DomainService.People;
using Identifier.DomainService.Projects;
using Identifier.DomainService.Shared.Services;
using Identifier.DomainService.Shared.Utilities;
using Identifier.DomainService.Subscription.Services;
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
            // Register validator
            services.AddTransient<IValidator<CreateProjectRequest>, CreateProjectRequestValidator>();
            services.AddTransient<IValidator<UpdateAuthConfigRequest>, UpdateAuthConfigRequestValidator>();
            services.AddTransient<IValidator<UpdateProjectRequest>, UpdateProjectRequestValidator>();
            services.AddTransient<IValidator<SignupRequest>, SignupRequestValidator>();
            services.AddTransient<IValidator<TransferOwnershipRequest>, TransferOwnershipRequestValidator>();
            services.AddTransient<IValidator<MigrationRequest>, MigrationRequestValidator>();
            services.AddTransient<IValidator<RegisterServiceRequest>, RegisterServiceRequestValidator>();


            // Register services
            services.AddSingleton<IProjectManagementService, ProjectManagementService>();
            services.AddSingleton<IProjectRepository, ProjectRepository>();

            services.AddSingleton<IPeopleService, PeopleService>();
            services.AddSingleton<IPeopleRepository, PeopleRepository>();
            services.AddSingleton<IDomainManagementService, DomainManagementService>();
            services.AddSingleton<IMigrationService, MigrationService>();
            services.AddSingleton<IMigrationRepository, MigrationRepository>();
            services.AddSingleton<IMigrationNotificationService, MigrationNotificationService>();
            services.AddSingleton<ICertificateManager, CertificateManager>();
            services.AddSingleton<ICertificateStorageFactory, CertificateStorageFactory>();
            services.AddSingleton<IEncodingService, EncodingService>();
            services.AddSingleton<IServiceManagement, ServiceManagement>();
            services.AddSingleton<IServiceManagementRepository, ServiceManagementRepository>();
            services.AddSingleton<ISubscriptionRepository, SubscriptionRepository>();
            services.AddSingleton<ISubscriptionService, SubscriptionService>();

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
