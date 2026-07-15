using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class GithubLogInServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IHttpService> _http = new();

        private GithubLogInService Create() =>
            new(NullLogger<GithubLogInService>.Instance, _authRepo.Object, _http.Object);

        private static IdentityProvider Provider() => new()
        {
            Provider = "github",
            ProviderType = "social",
            ClientId = "client-1",
            ClientSecret = "secret-1",
            TokenEndpointAuthMethod = "client_secret_post",
            TokenUrl = "https://github/token",
            UserInfoUrl = "https://github/user",
            InitialRoles = new List<string> { "init-role" },
            InitialPermissions = new List<string> { "perm-1" }
        };

        private static StateInfo State() => new()
        {
            ClientId = "client-1",
            Provider = "github",
            Audience = "my-app",
            Code = "auth-code",
            RedirectUri = "https://app/callback"
        };

        private void SetupToken(SocialOauthAccessToken? token, string error = "")
        {
            _http.Setup(h => h.SendFormUrlEncoded<SocialOauthAccessToken>(
                    It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((token!, error));
        }

        private void SetupUser(GithubUserData? user, string error = "")
        {
            _http.Setup(h => h.Get<GithubUserData>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((user!, error));
        }

        private void SetupEmails(List<GithubEmail>? emails, string error = "")
        {
            _http.Setup(h => h.Get<List<GithubEmail>>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((emails!, error));
        }

        [Fact]
        public async Task ReturnsEmpty_WhenIdentityProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<GithubUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenTokenExchangeFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "bad_verification_code");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<GithubUserData>();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenAccessTokenNull()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = null });

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<GithubUserData>();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenUserFetchFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupUser(new GithubUserData(), "user boom");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<GithubUserData>();
        }

        [Fact]
        public async Task MapsUser_WhenEmailAlreadyPresent_SkipsEmailEndpoint()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", RefreshToken = "rt-1", IdToken = "it-1" });
            SetupUser(new GithubUserData { Id = 42, Email = "gh@user.com", DisplayName = "GH", Login = "ghlogin" });

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<GithubUserData>().Subject;
            user.Email.Should().Be("gh@user.com");
            user.ExternalProviderUserId.Should().Be("42");
            user.Platform.Should().Be("github");
            user.Roles.Should().ContainSingle().Which.Should().Be("init-role");
            user.Permissions.Should().ContainSingle().Which.Should().Be("perm-1");
            result.AccessToken.Should().Be("at-1");
            result.RefreshToken.Should().Be("rt-1");
            result.IdToken.Should().Be("it-1");

            _http.Verify(h => h.Get<List<GithubEmail>>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()), Times.Never);
        }

        [Fact]
        public async Task FetchesPrimaryVerifiedEmail_WhenUserEmailEmpty()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupUser(new GithubUserData { Id = 7, Email = null });
            SetupEmails(new List<GithubEmail>
            {
                new() { Email = "secondary@x.com", Primary = false, Verified = true },
                new() { Email = "primary@x.com", Primary = true, Verified = true },
                new() { Email = "unverified@x.com", Primary = false, Verified = false }
            });

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<GithubUserData>().Subject;
            user.Email.Should().Be("primary@x.com");
            user.ExternalProviderUserId.Should().Be("7");
        }

        [Fact]
        public async Task FetchesFirstVerifiedEmail_WhenNoPrimary()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupUser(new GithubUserData { Id = 8, Email = "" });
            SetupEmails(new List<GithubEmail>
            {
                new() { Email = "unverified@x.com", Primary = false, Verified = false },
                new() { Email = "verified@x.com", Primary = false, Verified = true }
            });

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().Be("verified@x.com");
        }

        [Fact]
        public async Task FallsBackToFirstEmail_WhenNoneVerified()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupUser(new GithubUserData { Id = 9, Email = "" });
            SetupEmails(new List<GithubEmail>
            {
                new() { Email = "first@x.com", Primary = false, Verified = false },
                new() { Email = "second@x.com", Primary = false, Verified = false }
            });

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Email.Should().Be("first@x.com");
        }

        [Fact]
        public async Task ReturnsPartialUser_WhenEmailEndpointFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", RefreshToken = "rt-1" });
            SetupUser(new GithubUserData { Id = 10, Email = "", DisplayName = "NoEmail" });
            SetupEmails(null, "emails boom");

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<GithubUserData>().Subject;
            user.DisplayName.Should().Be("NoEmail");
            user.Email.Should().BeNullOrEmpty();
            // early return still carries the token
            result.AccessToken.Should().Be("at-1");
            result.RefreshToken.Should().Be("rt-1");
        }

        [Fact]
        public async Task HandleSocialLogin_ReturnsExternalUserData()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupUser(new GithubUserData { Id = 11, Email = "x@x.com" });

            var user = await Create().HandleSocialLogin(State());

            user.Should().BeOfType<GithubUserData>();
            user.ExternalProviderUserId.Should().Be("11");
        }
    }
}
