using System.Reflection;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Guards the composition root the hosts actually use. Api and Worker both call
    /// <see cref="ApplicationServiceCollectionExtensions.RegisterAllServices"/>; a service registered only in
    /// Iam.DomainService's <c>RegisterSharedServices</c> (which only unit tests call) leaves its controller or
    /// consumer unconstructable at runtime, so every request fails with 500. This is what happened to
    /// <c>BatchController</c> (#683). Every controller, every Worker consumer and every registered
    /// implementation must find each of its own-code constructor dependencies in that registration.
    /// </summary>
    public sealed class HostCompositionTests
    {
        private static readonly HashSet<string> OwnAssemblies = new(StringComparer.Ordinal)
        {
            "Api", "Worker", "Authentication.DomainService", "Iam.DomainService", "Mfa.DomainService", "Captcha.Driver"
        };

        private static IServiceCollection Registered()
        {
            var services = new ServiceCollection();
            services.RegisterAllServices();
            return services;
        }

        [Fact]
        public void RegisterAllServices_RegistersTheBatchServices()
        {
            var services = Registered();
            services.Should().Contain(d => d.ServiceType == typeof(Iam.DomainService.Batch.IBatchJobRepository) && d.Lifetime == ServiceLifetime.Singleton);
            services.Should().Contain(d => d.ServiceType == typeof(Iam.DomainService.Batch.BatchUserStores) && d.Lifetime == ServiceLifetime.Singleton);
            services.Should().Contain(d => d.ServiceType == typeof(Iam.DomainService.Batch.IBatchUserService) && d.Lifetime == ServiceLifetime.Singleton);
        }

        [Fact]
        public void EveryController_CanResolveItsOwnCodeDependencies()
        {
            var controllers = typeof(global::Api.Controllers.BatchController).Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

            MissingDependencies(controllers).Should().BeEmpty("Api builds its controllers from RegisterAllServices");
        }

        [Fact]
        public void EveryWorkerConsumer_CanResolveItsOwnCodeDependencies()
        {
            var consumers = typeof(global::Worker.Consumers.BatchJobConsumer).Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(IsConsumer));

            MissingDependencies(consumers).Should().BeEmpty("Worker builds its consumers from RegisterAllServices");
        }

        [Fact]
        public void EveryRegisteredImplementation_CanResolveItsOwnCodeDependencies()
        {
            var implementations = Registered()
                .Select(d => d.ImplementationType)
                .Where(t => t is not null && IsOwn(t))
                .Select(t => t!);

            MissingDependencies(implementations).Should().BeEmpty("a registered service must be constructable from the same registration");
        }

        private static List<string> MissingDependencies(IEnumerable<Type> types)
        {
            var registered = Registered().Select(d => d.ServiceType).ToHashSet();
            return types
                .Distinct()
                .SelectMany(t => GreediestConstructorParameters(t).Select(p => (Owner: t, Parameter: p)))
                .Where(x => !x.Parameter.HasDefaultValue && IsOwn(x.Parameter.ParameterType))
                .Where(x => !IsRegistered(registered, x.Parameter.ParameterType))
                .Select(x => $"{x.Owner.FullName} needs {x.Parameter.ParameterType.FullName}")
                .Distinct()
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsRegistered(HashSet<Type> registered, Type type) =>
            registered.Contains(type) ||
            (type.IsGenericType && registered.Contains(type.GetGenericTypeDefinition()));

        private static bool IsOwn(Type type) => OwnAssemblies.Contains(type.Assembly.GetName().Name ?? string.Empty);

        private static bool IsConsumer(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IConsumer<>);

        private static IEnumerable<ParameterInfo> GreediestConstructorParameters(Type type) =>
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
                ?.GetParameters() ?? [];
    }
}
