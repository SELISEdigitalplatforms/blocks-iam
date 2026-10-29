using Authentication.DomainService.OAuth;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.TestDoubles;

/// <summary>Shared doubles for OIDC client secret handling.</summary>
internal static class OidcClientSecretTestDoubles
{
    /// <summary>
    /// A real <see cref="OidcClientSecretResolver"/> whose DI scopes serve
    /// <paramref name="secrets"/> as the scoped <see cref="ISecretService"/>. Without one, a strict
    /// mock throws on any secret call.
    /// </summary>
    /// <remarks>
    /// As with <see cref="ClientCredentialSecretTestDoubles"/>: <c>RetireAsync</c> logs any
    /// non-cancellation failure and a token-request read turns it into
    /// <see cref="OidcClientSecretUnavailableException"/>, so strictness alone does not prove
    /// "no delete" or "no read". Assert it with <c>Verify(..., Times.Never)</c>.
    /// </remarks>
    public static OidcClientSecretResolver Resolver(
        Mock<ISecretService>? secrets = null,
        ILogger<OidcClientSecretResolver>? logger = null)
    {
        var secretService = (secrets ?? new Mock<ISecretService>(MockBehavior.Strict)).Object;
        var services = new ServiceCollection();
        services.AddScoped(_ => secretService);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return new OidcClientSecretResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<OidcClientSecretResolver>.Instance);
    }

    /// <summary>
    /// A secret store that accepts OIDC client secret writes (stores answer
    /// <paramref name="reference"/>, rotations succeed), for tests about something else.
    /// </summary>
    public static Mock<ISecretService> AcceptingStore(string reference = "oidc-ref")
    {
        var secrets = new Mock<ISecretService>();
        secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(reference);
        secrets.Setup(s => s.RotateAsync(It.IsAny<string>(), It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return secrets;
    }
}
