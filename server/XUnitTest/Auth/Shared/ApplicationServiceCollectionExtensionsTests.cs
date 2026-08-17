using Authentication.DomainService.Authentication;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using FluentAssertions;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Mfa.DomainService.Services;
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
        }

        [Fact]
        public void RegisterAllServices_RegistersIamAndMfaContracts()
        {
            var services = Registered();

            services.Should().Contain(d => d.ServiceType == typeof(IUserRepository));
            services.Should().Contain(d => d.ServiceType == typeof(IIdentityAccessManagementRepository));
            services.Should().Contain(d => d.ServiceType == typeof(IResourceRepository));
            services.Should().Contain(d => d.ServiceType == typeof(IMfaManagementRepository));
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
