using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Utilities;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Mfa.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Unit tests for <see cref="ApplicationServiceCollectionExtensions"/>. Registering the full graph
    /// once exercises every registration line and asserts that the key contracts each host relies on are
    /// present with their expected lifetimes.
    /// </summary>
    public sealed class ApplicationServiceCollectionExtensionsTests
    {
        private static IServiceCollection Registered()
        {
            var services = new ServiceCollection();
            services.RegisterAllServices();
            return services;
        }

        [Fact]
        public void RegisterAllServices_RegistersCoreAuthenticationContracts()
        {
            var services = Registered();

            services.Should().Contain(d => d.ServiceType == typeof(IAuthenticationRepository) && d.Lifetime == ServiceLifetime.Singleton);
            services.Should().Contain(d => d.ServiceType == typeof(IAuthenticationService) && d.Lifetime == ServiceLifetime.Singleton);
            services.Should().Contain(d =>
                d.ServiceType == typeof(IValidator<SaveOidcUiTemplateRequest>) &&
                d.ImplementationType == typeof(SaveOidcUiTemplateRequestValidator) &&
                d.Lifetime == ServiceLifetime.Transient);
        }

        [Fact]
        public void RegisterAllServicesRegistersDelegatedAdministrationAndBindsItsMode()
        {
            var services = Registered();
            services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["DelegatedAdministration:Mode"] = "Enforce" })
                    .Build());

            services.Should().Contain(d => d.ServiceType == typeof(Iam.DomainService.DelegatedAdministration.IDelegatedAdministrationService) && d.Lifetime == ServiceLifetime.Singleton);
            services.Should().Contain(d => d.ServiceType == typeof(Iam.DomainService.DelegatedAdministration.IDelegatedAdministrationRepository));
            services.Should().Contain(d => d.ServiceType == typeof(IClientCredentialDelegationGuard));
            services.Should().NotContain(d => d.ServiceType.Name == "IGrantAuthorizationService");

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Iam.DomainService.DelegatedAdministration.DelegatedAdministrationOptions>>()
                .CurrentValue.Mode.Should().Be(Iam.DomainService.DelegatedAdministration.DelegatedAdministrationMode.Enforce);
        }

        [Fact]
        public void DelegatedAdministrationModeDefaultsToAuditWhenNotConfigured()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            Iam.DomainService.DelegatedAdministration.DelegatedAdministrationServiceCollectionExtensions.AddDelegatedAdministration(services);

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Iam.DomainService.DelegatedAdministration.DelegatedAdministrationOptions>>()
                .CurrentValue.Mode.Should().Be(Iam.DomainService.DelegatedAdministration.DelegatedAdministrationMode.Audit);
        }

        [Fact]
        public void RegisterAllServices_RegistersIamAndMfaContracts()
        {
            var services = Registered();

            services.Should().Contain(d => d.ServiceType == typeof(IUserRepository));
            services.Should().Contain(d => d.ServiceType == typeof(IIdentityAccessManagementRepository));
            services.Should().Contain(d => d.ServiceType == typeof(IResourceRepository));
            services.Should().Contain(d => d.ServiceType == typeof(IMfaManagementRepository));
            // Shared by AccountService and SsoUserProvisioningService, both singletons.
            services.Should().Contain(d =>
                d.ServiceType == typeof(Iam.DomainService.Accounts.ISignupOrganizationResolver)
                && d.ImplementationType == typeof(Iam.DomainService.Accounts.SignupOrganizationResolver)
                && d.Lifetime == ServiceLifetime.Singleton);
        }

        [Fact]
        public void RegisterAllServices_RegistersMultipleExternalUserMappers()
        {
            var services = Registered();

            var mapperCount = services.Count(d => d.ServiceType == typeof(Authentication.DomainService.OAuth.SocialServices.IExternalUserMapper));
            mapperCount.Should().BeGreaterThan(1);
        }

        [Fact]
        public void RegisterAllServices_RegistersDeviceCleanupHostedService()
        {
            var services = Registered();

            // Assert the specific worker, not merely that some IHostedService exists: with two
            // hosted services registered, the weaker check passes even if one is missing entirely.
            services.Should().Contain(d =>
                d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
                d.ImplementationType == typeof(Authentication.DomainService.Oidc.Services.DeviceCleanupWorker));
        }

        [Fact]
        public void RegisterAllServices_RegistersBlacklistIndexHostedService()
        {
            var services = Registered();

            services.Should().Contain(d =>
                d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
                d.ImplementationType == typeof(Authentication.DomainService.Oidc.Services.BlacklistIndexWorker));
        }

        [Fact]
        public void RegisterAllServices_IsIdempotentEnoughToRegisterManyDescriptors()
        {
            var services = Registered();
            services.Count.Should().BeGreaterThan(50);
        }
    }
}
