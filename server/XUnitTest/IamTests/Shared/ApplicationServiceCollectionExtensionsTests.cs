using FluentAssertions;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// Verifies <see cref="ApplicationServiceCollectionExtensions.RegisterSharedServices"/> wires up
    /// the IAM services and validators with the expected lifetimes.
    /// </summary>
    public class ApplicationServiceCollectionExtensionsTests
    {
        private static ServiceCollection Registered()
        {
            var services = new ServiceCollection();
            services.RegisterSharedServices();
            return services;
        }

        [Fact]
        public void RegisterSharedServices_RegistersCoreServices_AsSingleton()
        {
            var services = Registered();

            foreach (var name in new[]
            {
                "IUserManagementMutationService",
                "IUserRepository",
                "IIdentityAccessManagementService",
                "IIdentityAccessManagementRepository",
                "IResourceMutationService",
                "IResourceRepository",
                "IUserManagementQueryService",
                "IResourceQueryService",
                "IAccountService",
                "IIamConfigurationRepository"
            })
            {
                services.Should().Contain(
                    d => d.ServiceType.Name == name && d.Lifetime == ServiceLifetime.Singleton,
                    $"{name} should be registered as a singleton");
            }
        }

        [Fact]
        public void RegisterSharedServices_RegistersValidators_AsTransient()
        {
            var services = Registered();

            var validatorRegistrations = services
                .Where(d => d.ServiceType.Name == "IValidator`1")
                .ToList();

            validatorRegistrations.Should().NotBeEmpty();
            validatorRegistrations
                .Count(d => d.Lifetime == ServiceLifetime.Transient)
                .Should().BeGreaterThan(0);
        }

        [Fact]
        public void RegisterSharedServices_RegistersHttpContextAccessor()
        {
            var services = Registered();

            services.Should().Contain(d => d.ServiceType.Name == "IHttpContextAccessor");
        }
    }
}
