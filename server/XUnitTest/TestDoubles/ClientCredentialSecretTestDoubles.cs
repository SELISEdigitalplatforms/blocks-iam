using Authentication.DomainService.OAuth;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.TestDoubles;

/// <summary>Shared doubles for client-credential secret handling.</summary>
internal static class ClientCredentialSecretTestDoubles
{
    /// <summary>
    /// A real <see cref="ClientCredentialSecretResolver"/> whose DI scopes serve
    /// <paramref name="secrets"/> as the scoped <see cref="ISecretService"/>. Without one, a strict
    /// mock throws on any secret call.
    /// </summary>
    /// <remarks>
    /// As with <see cref="IdentityProviderSecretTestDoubles"/>: <c>RetireAsync</c> logs any
    /// non-cancellation failure and a token-request read turns it into
    /// <see cref="ClientCredentialSecretUnavailableException"/>, so strictness alone does not prove
    /// "no delete" or "no read". Assert it with <c>Verify(..., Times.Never)</c>.
    /// </remarks>
    public static ClientCredentialSecretResolver Resolver(
        Mock<ISecretService>? secrets = null,
        ILogger<ClientCredentialSecretResolver>? logger = null)
    {
        var secretService = (secrets ?? new Mock<ISecretService>(MockBehavior.Strict)).Object;
        var services = new ServiceCollection();
        services.AddScoped(_ => secretService);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return new ClientCredentialSecretResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<ClientCredentialSecretResolver>.Instance);
    }
}
