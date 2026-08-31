using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class SocialAuthorizationServiceBaseTests
    {
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ISocialLogInServiceProvider> _provider = new();

        // Concrete test double: exposes the abstract GetUser via an injected delegate.
        private sealed class TestSocialAuthorizationService : SocialAuthorizationServiceBase
        {
            private readonly Func<StateInfo, IExternalUserData, (User?, string)> _getUser;

            public TestSocialAuthorizationService(
                IOAuthJwtAccessTokenManager tokenManager,
                IAuthenticationRepository repo,
                ICacheClient cache,
                ISocialLogInServiceProvider provider,
                Func<StateInfo, IExternalUserData, (User?, string)> getUser)
                : base(NullLogger.Instance, tokenManager, repo, cache, provider)
            {
                _getUser = getUser;
            }

            public override Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
                => Task.FromResult(_getUser(stateInfo, externalUser));
        }

        private TestSocialAuthorizationService Create(Func<StateInfo, IExternalUserData, (User?, string)> getUser)
            => new(_tokenManager.Object, _repo.Object, _cache.Object, _provider.Object, getUser);

        private static TokenRequest Request() => new() { Code = "auth-code", State = "state-1", OrganizationId = "org-9" };

        private static string ValidStateJson() => JsonSerializer.Serialize(new StateInfo
        {
            ClientId = "client-1",
            Provider = "google",
            Audience = "aud-1"
        });

        private static BYOSsoUserData ExternalUser(string email = "ext@user.com", string extId = "ext-123")
            => new() { Email = email, ExternalProviderUserId = extId };

        private void SetupCallback(IExternalUserData externalUser)
        {
            _cache.Setup(c => c.GetStringValueAsync("state-1")).ReturnsAsync(ValidStateJson());
            _provider.Setup(p => p.HandleSocialLoginCallback(It.IsAny<StateInfo>()))
                .ReturnsAsync(new SocialCallbackResult { ExternalUserData = externalUser });
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
        }

        private void SetupTokenSuccess()
        {
            _tokenManager.Setup(t => t.ManageTokenAsync(
                    It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "tok" });
            _repo.Setup(r => r.UpdatePartialAsync<User>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task Code_Empty_ReturnsCodeRequire()
        {
            var svc = Create((_, _) => (null, ""));

            var result = await svc.AuthenticateAsync(new TokenRequest { Code = "", State = "state-1" }, new IdentityConfiguration());

            result.Error.Should().Be("code_require");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task State_Empty_ReturnsStateRequire()
        {
            var svc = Create((_, _) => (null, ""));

            var result = await svc.AuthenticateAsync(new TokenRequest { Code = "auth-code", State = "" }, new IdentityConfiguration());

            result.Error.Should().Be("state_require");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task StateData_NotFound_ReturnsStateDataNotFound()
        {
            _cache.Setup(c => c.GetStringValueAsync("state-1")).ReturnsAsync((string)null!);
            var svc = Create((_, _) => (null, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("state_data_not_found");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task StateData_Invalid_ReturnsStateDataInvalid()
        {
            _cache.Setup(c => c.GetStringValueAsync("state-1")).ReturnsAsync("null");
            var svc = Create((_, _) => (null, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("state_data_invalid");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task Email_NotProvided_ReturnsError_AndRemovesState()
        {
            SetupCallback(ExternalUser(email: ""));
            var svc = Create((_, _) => (null, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("External provider did not provide any email.");
            result.StatusCode.Should().Be(401);
            _cache.Verify(c => c.RemoveKeyAsync("state-1"), Times.Once);
        }

        [Fact]
        public async Task ExternalUserId_Missing_ReturnsError()
        {
            SetupCallback(ExternalUser(extId: ""));
            var svc = Create((_, _) => (null, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("External provider did not provide any user id.");
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task UserNull_WithRedirect_ReturnsRedirectUrl()
        {
            SetupCallback(ExternalUser());
            var svc = Create((_, _) => (null, "https://redirect/consent"));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.SsoUserRedirectUrl.Should().Be("https://redirect/consent");
        }

        [Fact]
        public async Task UserNull_NoRedirect_ReturnsUserNotFound()
        {
            SetupCallback(ExternalUser());
            var svc = Create((_, _) => (null, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("user_not_found");
            result.ErrorDescription.Should().Contain("does not exist");
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task User_NotActive_ReturnsError()
        {
            SetupCallback(ExternalUser());
            var user = new User { ItemId = "u1", Active = false, IsVerified = true };
            var svc = Create((_, _) => (user, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Contain("not active");
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task User_Locked_ReturnsAccountLocked()
        {
            SetupCallback(ExternalUser());
            var user = new User
            {
                ItemId = "u1",
                Active = true,
                IsVerified = true,
                LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };
            var svc = Create((_, _) => (user, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be(OAuthError.AccountLocked);
            result.StatusCode.Should().Be(423);
        }

        [Fact]
        public async Task HappyPath_OrgDiffers_UpdatesUser_AndReturnsToken()
        {
            SetupCallback(ExternalUser());
            SetupTokenSuccess();
            var user = new User
            {
                ItemId = "u1",
                Active = true,
                IsVerified = true,
                OrganizationIds = new List<string> { "org-9" },
                LastUsedOrganizationId = "org-1"
            };
            var svc = Create((_, _) => (user, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.AccessToken.Should().Be("tok");
            _repo.Verify(r => r.UpdatePartialAsync<User>(
                "u1", It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task HappyPath_OrgSame_DoesNotUpdateUser()
        {
            SetupCallback(ExternalUser());
            SetupTokenSuccess();
            var user = new User
            {
                ItemId = "u1",
                Active = true,
                IsVerified = true,
                OrganizationIds = new List<string> { "org-9" },
                LastUsedOrganizationId = "org-9"
            };
            var svc = Create((_, _) => (user, ""));

            var result = await svc.AuthenticateAsync(Request(), new IdentityConfiguration());

            result.AccessToken.Should().Be("tok");
            _repo.Verify(r => r.UpdatePartialAsync<User>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
        }
    }
}
