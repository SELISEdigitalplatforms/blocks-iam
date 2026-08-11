using System.Text.Json;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Dtos;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class OidcCallbackHandlerTests
    {
        private static OidcCallbackHandler Create(
            out Mock<IAuthenticationRepository> authRepo,
            out Mock<ICacheClient> cache,
            out Mock<IUserRepository> userRepo,
            out Mock<ISocialLogInServiceProvider> socialProvider)
        {
            return Create(out authRepo, out cache, out userRepo, out socialProvider, out _, out _);
        }

        private static OidcCallbackHandler Create(
            out Mock<IAuthenticationRepository> authRepo,
            out Mock<ICacheClient> cache,
            out Mock<IUserRepository> userRepo,
            out Mock<ISocialLogInServiceProvider> socialProvider,
            out Mock<IResourceMutationService> resourceMutation,
            out Mock<IResourceRepository> resourceRepo)
        {
            authRepo = new Mock<IAuthenticationRepository>();
            cache = new Mock<ICacheClient>();
            userRepo = new Mock<IUserRepository>();
            socialProvider = new Mock<ISocialLogInServiceProvider>();
            resourceMutation = new Mock<IResourceMutationService>();
            resourceRepo = new Mock<IResourceRepository>();

            return new OidcCallbackHandler(
                NullLogger<OidcCallbackHandler>.Instance,
                authRepo.Object, cache.Object, userRepo.Object, socialProvider.Object,
                resourceMutation.Object, resourceRepo.Object);
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
            var stateJson = JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = "", Provider = "google" });
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(stateJson);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid OIDC context");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenProviderMissing()
        {
            var handler = Create(out _, out var cache, out _, out _);
            var stateJson = JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = "oidc-state-1", Provider = "" });
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(stateJson);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Provider not found");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenOidcContextMissing()
        {
            var handler = Create(out _, out var cache, out _, out _);
            var stateJson = JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = "oidc-state-1", Provider = "google" });
            cache.SetupSequence(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync(stateJson)
                .ReturnsAsync((string?)null);

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("OIDC flow expired");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsError_WhenExternalUserMissingEmail()
        {
            var handler = Create(out _, out var cache, out _, out var social);
            var stateJson = JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = "oidc-state-1", Provider = "google" });
            var contextJson = JsonSerializer.Serialize(new OidcContext
            {
                ClientId = "client-1",
                RedirectUri = "https://redirect.com",
                State = "state-1",
                Scope = "openid",
                Nonce = "nonce-1"
            });

            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(stateJson);
            cache.Setup(c => c.GetStringValueAsync("oidc_context:oidc-state-1")).ReturnsAsync(contextJson);
            social.Setup(s => s.HandleSocialLoginCallback(It.IsAny<StateInfo>()))
                .ReturnsAsync(new SocialCallbackResult { ExternalUserData = new BYOSsoUserData() });

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("did not return a valid user email");
        }

        [Fact]
        public async Task HandleCallbackAsync_ReturnsSuccess_AndCleansUpCache_WhenExistingUser()
        {
            var handler = Create(out _, out var cache, out var userRepo, out var social);
            var stateJson = JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = "oidc-state-1", Provider = "google" });
            var contextJson = JsonSerializer.Serialize(new OidcContext
            {
                ClientId = "client-1",
                RedirectUri = "https://redirect.com",
                State = "state-1",
                Scope = "openid",
                Nonce = "nonce-1",
                CodeChallenge = "challenge",
                CodeChallengeMethod = "S256"
            });

            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(stateJson);
            cache.Setup(c => c.GetStringValueAsync("oidc_context:oidc-state-1")).ReturnsAsync(contextJson);

            var externalUser = new BYOSsoUserData { Email = "u@example.com", FirstName = "U", LastName = "S" };
            social.Setup(s => s.HandleSocialLoginCallback(It.IsAny<StateInfo>()))
                .ReturnsAsync(new SocialCallbackResult { ExternalUserData = externalUser });

            userRepo.Setup(r => r.GetUserByEmailAsync("u@example.com"))
                .ReturnsAsync(new User { ItemId = "existing-user", Email = "u@example.com", Active = true });

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
        public async Task HandleCallbackAsync_CreatesNewUser_WhenEmailNotFound()
        {
            var handler = Create(out _, out var cache, out var userRepo, out var social);
            var stateJson = JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = "oidc-state-1", Provider = "google" });
            var contextJson = JsonSerializer.Serialize(new OidcContext
            {
                ClientId = "client-1",
                RedirectUri = "https://redirect.com",
                State = "state-1",
                Scope = "openid",
                Nonce = "nonce-1"
            });

            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(stateJson);
            cache.Setup(c => c.GetStringValueAsync("oidc_context:oidc-state-1")).ReturnsAsync(contextJson);

            var externalUser = new BYOSsoUserData
            {
                Email = "new@example.com",
                FirstName = "New",
                LastName = "User"
            };
            social.Setup(s => s.HandleSocialLoginCallback(It.IsAny<StateInfo>()))
                .ReturnsAsync(new SocialCallbackResult { ExternalUserData = externalUser });

            userRepo.Setup(r => r.GetUserByEmailAsync("new@example.com")).ReturnsAsync((User?)null);
            userRepo.Setup(r => r.CreateUserAsync(It.IsAny<User>())).Returns(Task.FromResult(true));

            var result = await handler.HandleCallbackAsync("code-1", "state-1");

            result.IsSuccess.Should().BeTrue();
            userRepo.Verify(r => r.CreateUserAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task HandleCallbackAsync_DefaultsTenantId_ToDefault()
        {
            var handler = Create(out _, out var cache, out var userRepo, out var social);
            var stateJson = JsonSerializer.Serialize(new OidcSocialStateContext { OidcState = "oidc-state-1", Provider = "google" });
            var contextJson = JsonSerializer.Serialize(new OidcContext
            {
                ClientId = "client-1",
                RedirectUri = "https://redirect.com",
                State = "state-1",
                Scope = "openid",
                TenantId = ""
            });

            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(stateJson);
            cache.Setup(c => c.GetStringValueAsync("oidc_context:oidc-state-1")).ReturnsAsync(contextJson);

            var externalUser = new BYOSsoUserData { Email = "u@example.com" };
            social.Setup(s => s.HandleSocialLoginCallback(It.IsAny<StateInfo>()))
                .ReturnsAsync(new SocialCallbackResult { ExternalUserData = externalUser });

            userRepo.Setup(r => r.GetUserByEmailAsync("u@example.com"))
                .ReturnsAsync(new User { ItemId = "u1", Email = "u@example.com", Active = true });

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