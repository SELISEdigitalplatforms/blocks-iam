using System.Text.Json;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Dtos;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class OidcCallbackHandlerTests
    {
        private static OidcCallbackHandler Create(
            out Mock<IAuthenticationRepository> authRepo,
            out Mock<ICacheClient> cache,
            out Mock<ISsoUserProvisioningService> provisioning,
            out Mock<ISocialLogInServiceProvider> socialProvider)
        {
            authRepo = new Mock<IAuthenticationRepository>();
            cache = new Mock<ICacheClient>();
            provisioning = new Mock<ISsoUserProvisioningService>();
            socialProvider = new Mock<ISocialLogInServiceProvider>();

            return new OidcCallbackHandler(
                NullLogger<OidcCallbackHandler>.Instance,
                authRepo.Object, cache.Object, socialProvider.Object, provisioning.Object);
        }

        private static string SocialStateJson(string oidcState = "oidc-state-1", string provider = "google")
            => JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = oidcState, Provider = provider });

        private static string ContextJson(string? tenantId = null)
            => JsonSerializer.Serialize(new OidcContext
            {
                ClientId = "client-1",
                RedirectUri = "https://redirect.com",
                State = "state-1",
                Scope = "openid",
                Nonce = "nonce-1",
                CodeChallenge = "challenge",
                CodeChallengeMethod = "S256",
                TenantId = tenantId
            });

        /// <summary>
        /// Drives the handler as far as the provisioning call, with the given result waiting there.
        /// </summary>
        private static OidcCallbackHandler ArrangeUpToProvisioning(
            SsoProvisioningResult provisioningResult,
            out Mock<ICacheClient> cache,
            out Mock<ISsoUserProvisioningService> provisioning,
            string? tenantId = null)
        {
            var handler = Create(out _, out cache, out provisioning, out var social);

            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(SocialStateJson());
            cache.Setup(c => c.GetStringValueAsync("oidc_context:oidc-state-1")).ReturnsAsync(ContextJson(tenantId));

            social.Setup(s => s.HandleSocialLoginCallback(It.IsAny<StateInfo>()))
                .ReturnsAsync(new SocialCallbackResult
                {
                    ExternalUserData = new BYOSsoUserData { Email = "u@example.com", FirstName = "U", LastName = "S" }
                });

            provisioning
                .Setup(p => p.ResolveOrProvisionAsync(It.IsAny<IExternalUserData>(), It.IsAny<string>()))
                .ReturnsAsync(provisioningResult);

            return handler;
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenStateNotInCache()
        {
            var handler = Create(out _, out var cache, out _, out _);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid or expired OIDC state");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenOidcStateNull()
        {
            var handler = Create(out _, out var cache, out _, out _);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(SocialStateJson(oidcState: ""));

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid OIDC context");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenProviderMissing()
        {
            var handler = Create(out _, out var cache, out _, out _);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(SocialStateJson(provider: ""));

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Provider not found");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenOidcContextMissing()
        {
            var handler = Create(out _, out var cache, out _, out _);
            cache.SetupSequence(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync(SocialStateJson())
                .ReturnsAsync((string?)null);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("OIDC flow expired");
            result.ErrorCode.Should().Be("oidc_flow_expired");
            // Nothing was recovered, so there is no login page to send the browser back to.
            result.ClientId.Should().BeNull();
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenExternalUserMissingEmail()
        {
            var handler = Create(out _, out var cache, out _, out var social);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(SocialStateJson());
            cache.Setup(c => c.GetStringValueAsync("oidc_context:oidc-state-1")).ReturnsAsync(ContextJson());
            social.Setup(s => s.HandleSocialLoginCallback(It.IsAny<StateInfo>()))
                .ReturnsAsync(new SocialCallbackResult { ExternalUserData = new BYOSsoUserData() });

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("did not return a valid user email");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsSuccess_AndCleansUpCache_WhenExistingUser()
        {
            var existing = new User { ItemId = "existing-user", Email = "u@example.com", Active = true };
            var handler = ArrangeUpToProvisioning(
                SsoProvisioningResult.From(SsoProvisioningOutcome.ExistingUser, existing),
                out var cache,
                out _);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeTrue();
            result.IsOidcFlow.Should().BeTrue();
            result.BlocksUserId.Should().Be("existing-user");
            result.ClientId.Should().Be("client-1");
            result.RedirectUri.Should().Be("https://redirect.com");
            result.Scope.Should().Be("openid");
            result.Nonce.Should().Be("nonce-1");
            cache.Verify(c => c.RemoveKeyAsync(It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsSuccess_WhenUserProvisioned()
        {
            var created = new User { ItemId = "new-user", Email = "u@example.com", Active = true };
            var handler = ArrangeUpToProvisioning(
                SsoProvisioningResult.From(SsoProvisioningOutcome.Provisioned, created),
                out _,
                out var provisioning);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeTrue();
            result.BlocksUserId.Should().Be("new-user");
            provisioning.Verify(
                p => p.ResolveOrProvisionAsync(It.IsAny<IExternalUserData>(), "google"),
                Times.Once);
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenSsoSignupDisabled()
        {
            var handler = ArrangeUpToProvisioning(
                SsoProvisioningResult.From(SsoProvisioningOutcome.SignupDisabled),
                out _,
                out _);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("No account exists");
            result.ErrorCode.Should().Be("signup_disabled");
            // The refusal carries the original request so the controller can return the user to
            // the login page instead of leaving them on an error body.
            result.ClientId.Should().Be("client-1");
            result.RedirectUri.Should().Be("https://redirect.com");
            result.OriginalState.Should().Be("state-1");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenProvisioningFails()
        {
            var handler = ArrangeUpToProvisioning(
                SsoProvisioningResult.From(SsoProvisioningOutcome.Failed),
                out _,
                out _);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Failed to create user account");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenUserInactive()
        {
            var inactive = new User { ItemId = "u1", Email = "u@example.com", Active = false };
            var handler = ArrangeUpToProvisioning(
                SsoProvisioningResult.From(SsoProvisioningOutcome.ExistingUser, inactive),
                out _,
                out _);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not active");
        }

        [Fact]
        public async Task HandleCallbackAsync_DefaultsTenantId_ToDefault()
        {
            var existing = new User { ItemId = "u1", Email = "u@example.com", Active = true };
            var handler = ArrangeUpToProvisioning(
                SsoProvisioningResult.From(SsoProvisioningOutcome.ExistingUser, existing),
                out _,
                out _,
                tenantId: "");

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.TenantId.Should().Be("default");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_OnUnexpectedException()
        {
            var handler = Create(out _, out var cache, out _, out _);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new Exception("boom"));

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("error occurred");
        }
    }
}
