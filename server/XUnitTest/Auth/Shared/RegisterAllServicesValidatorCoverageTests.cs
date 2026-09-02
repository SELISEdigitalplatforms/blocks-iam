using System.Reflection;
using Authentication.DomainService.Utilities;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Users;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Guards the composition root that the hosts actually use. Iam.DomainService has its own
    /// <c>RegisterSharedServices</c>, but Api and Worker both call
    /// <see cref="ApplicationServiceCollectionExtensions.RegisterAllServices"/>, so a validator added
    /// only to the former leaves every controller in the graph unconstructable at runtime.
    /// </summary>
    public sealed class RegisterAllServicesValidatorCoverageTests
    {
        private static IServiceCollection Registered()
        {
            var services = new ServiceCollection();
            services.RegisterAllServices();
            return services;
        }

        [Fact]
        public void RegisterAllServices_RegistersMyAccountValidator()
        {
            Registered().Should().Contain(d =>
                d.ServiceType == typeof(IValidator<UpdateMyAccountRequest>) &&
                d.ImplementationType == typeof(UpdateMyAccountValidator));
        }

        [Fact]
        public void RegisterAllServices_RegistersEveryValidatorItsOwnImplementationsRequire()
        {
            var services = Registered();
            var registered = services.Select(d => d.ServiceType).ToHashSet();

            var missing = services
                .Select(d => d.ImplementationType)
                .Where(t => t is not null)
                .Distinct()
                .SelectMany(GreediestConstructorParameters)
                .Where(p => !p.HasDefaultValue && IsValidator(p.ParameterType))
                .Select(p => p.ParameterType)
                .Where(t => !registered.Contains(t))
                .Distinct()
                .Select(t => t.ToString())
                .ToList();

            missing.Should().BeEmpty("every IValidator<T> a registered implementation asks for must be registered too");
        }

        private static IEnumerable<ParameterInfo> GreediestConstructorParameters(Type? type) =>
            type is null
                ? []
                : type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                      .OrderByDescending(c => c.GetParameters().Length)
                      .FirstOrDefault()
                      ?.GetParameters() ?? [];

        private static bool IsValidator(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IValidator<>);
    }
}
