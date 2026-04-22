using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Utilities;
using DomainService.Worker;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Users;
using Mfa.DomainService.Configuration;
using Worker;
using Worker.Configuration;
using Worker.Consumers;
using Worker.Consumers.Users;

const string _serviceName = "blocks-idp-worker";

var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(_serviceName, VaultType.Azure);

await CreateHostBuilder(args).Build().RunAsync();

IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, builder) =>
        {
            ApplicationConfigurations.ConfigureWorkerEnv(builder, args);
        })
        .ConfigureServices((services) =>
        {
            services.AddHttpClient();

            services.Configure<VerioSystemSettings>(services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("VerioSystemSettings"));

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
            services.AddSingleton<IConsumer<UserStatusChangedEvent>, UserStatusChangedConsumer>();

            services.AddHostedService<PeriodicPingBackgroundService>();

            services.RegisterAllServices();

            ApplicationConfigurations.ConfigureWorker(services, IdpConstants.GetMessageConfiguration(secret.MessageConnectionString));
        });