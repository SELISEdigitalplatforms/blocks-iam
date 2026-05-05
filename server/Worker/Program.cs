using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Identifier.DomainService.Migration;
using Identifier.DomainService.Projects;
using Identifier.DomainService.Shared;
using Identifier.DomainService.Dtos;
using Identifier.DomainService.Shared.Dtos;
using Identifier.DomainService.Shared.Entities;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Worker;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Users;
using Mfa.DomainService.Configuration;
using Worker;
using Worker.Consumers;
using Worker.Consumers.Identifier;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile(GetEnvironmentAppSettingsFileName(), optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var serviceName = ResolveRequiredServiceName(configuration);

var vaultType = ApplicationConfigurations.ResolveVaultType();
Console.WriteLine($"Using Genesis vault type: {vaultType}");
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, vaultType);

await CreateHostBuilder(args).Build().RunAsync();

IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, builder) =>
        {
            // ApplicationConfigurations.ConfigureWorkerEnv(builder, args);
        })
        .ConfigureServices((services) =>
        {
            services.AddHttpClient();

            services.AddSingleton<IConsumer<RefreshTokenEvent>, RefreshTokenWorkerService>();
            services.AddSingleton<IConsumer<UserAuthenticationTimelineEvent>, UserAuthenticationTimelineWorkerService>();
            services.AddSingleton<IConsumer<MfaActionEvent>, UpdateMfaConfigurationService>();

            services.AddSingleton<IConsumer<ResourceMutationEvent>, ResourceMutationConsumer>();
            services.AddSingleton<IConsumer<ResourceSetToPermissionMutationEvent>, ResourceSetToPermissionMutationConsumer>();
            services.AddSingleton<IConsumer<UserMutationEvent>, UserMutationConsumer>();
            services.AddSingleton<IConsumer<AccountActivityEvent>, AccountActivityWorkerService>();
            services.AddSingleton<IConsumer<CreateUserByEmailEvent>, CreateUserByEmailConsumer>();
            services.AddSingleton<IConsumer<CreateUserRequest>, CreateUserConsumer>();
            services.AddSingleton<IConsumer<CreateUserViaSsoEvent>, CreateUserViaSsoConsumer>();

            services.AddHostedService<PeriodicPingBackgroundService>();

            services.RegisterAllServices();

           

            #region Identifier Service Consumers
            services.AddApplicationServices();
            services.AddSingleton<IConsumer<Tenant>, ConfigureProjectConsumer>();
            services.AddSingleton<IConsumer<DisableDomainBindingRequest>, DisableDomainBindingConsumer>();
            services.AddSingleton<IConsumer<RestoreProjectRequest>, RestoreProjectConsumer>();
            services.AddSingleton<IConsumer<CreateUserByEmailPostEvent_Identifier>, CreateUserByEmailPostConsumer>();
            services.AddSingleton<IConsumer<ConfigureDomainRequest>, DomainConfigureConsumer>();
            services.AddSingleton<IConsumer<MigrationCompletionEvent>, MigrationCompletionConsumer>();
            services.AddSingleton<IConsumer<EnvironmentDataMigrationEvent>, EnvironmentDataMigrationEventConsumer>();
            services.AddSingleton<IConsumer<PublishScheduleCommand>, DataCleanupConsumer>();
            services.AddSingleton<IConsumer<UpdateResourceUsageCommand_Identifier>, UpdateResourceUsageConsumer>();

            var workerMessageConfiguration = IdpConstants.GetMessageConfiguration(secret.MessageConnectionString);
            workerMessageConfiguration.ServiceName = serviceName;
            ApplicationConfigurations.ConfigureWorker(services, workerMessageConfiguration);
            //ApplicationConfigurations.ConfigureWorker(services, IdentifierConstants.GetMessageConfiguration(secret.MessageConnectionString));
            #endregion
        });

static string ResolveRequiredServiceName(IConfiguration configuration)
{
    var serviceName = Environment.GetEnvironmentVariable("ServiceName") ?? configuration["ServiceName"];
    if (string.IsNullOrWhiteSpace(serviceName))
    {
        throw new InvalidOperationException("Missing required ServiceName configuration.");
    }

    return serviceName;
}

static string GetEnvironmentAppSettingsFileName()
{
    var currentEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    return string.IsNullOrWhiteSpace(currentEnvironment) ? "appsettings.json" : $"appsettings.{currentEnvironment}.json";
}
