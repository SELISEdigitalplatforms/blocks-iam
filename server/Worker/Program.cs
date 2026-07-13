using Authentication.DomainService.Utilities;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;
using SeliseBlocks.ConfigurationDriver;
using Worker;
using Worker.Configuration;
using Worker.Consumers;

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
        builder.AddMongoDbConfiguration(options =>
        {
            options.ConnectionString = secret.DatabaseConnectionString;
            options.DatabaseName = secret.RootDatabaseName;
            options.CollectionName = "Secrets";
            options.SecretKey = "blocks-secret-iam";
        });
    })
    .ConfigureServices((services) =>
    {
        services.AddHttpClient();

        services.Configure<PeriodicPingConfiguration>(
            configuration.GetSection("PeriodicPingConfiguration"));

        services.AddSingleton<IConsumer<ResourceMutationEvent>, ResourceMutationConsumer>();
        services.AddSingleton<IConsumer<ResourceSetToPermissionMutationEvent>, ResourceSetToPermissionMutationConsumer>();
        services.AddSingleton<IConsumer<UserMutationEvent>, UserMutationConsumer>();
        services.AddSingleton<IConsumer<UserActivityEvent>, UserActivityWorker>();
        services.AddSingleton<IConsumer<CreateUserByEmailEvent>, CreateUserByEmailConsumer>();
        services.AddSingleton<IConsumer<CreateUserRequest>, CreateUserConsumer>();
        services.AddSingleton<IConsumer<CreateUserViaSsoEvent>, CreateUserViaSsoConsumer>();
        services.AddSingleton<IConsumer<OrganizationProvisioningEvent>, OrganizationProvisioningConsumer>();
        services.AddSingleton<IConsumer<UpdateOrganizationUserEvent>, UpdateOrganizationUserConsumer>();
        services.AddSingleton<IConsumer<PermissionMutationForTenantsEvent>, PermissionMutationForTenantsConsumer>();
        services.AddSingleton<IConsumer<PropagationRolePermissionUpdateEvent>, PropagationRolePermissionUpdateConsumer>();

        services.AddHostedService<PeriodicPingBackgroundService>();

        services.RegisterAllServices();



        #region Identifier Service Consumers
        var workerMessageConfiguration = IdpConstants.GetMessageConfiguration(secret.MessageConnectionString);
        workerMessageConfiguration.ServiceName = serviceName;
        ApplicationConfigurations.ConfigureWorker(services, workerMessageConfiguration);
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
