using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.SocialServices;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class TwitterLogInServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IHttpService> _http = new();

        private TwitterLogInService Create() =>
            new(NullLogger<TwitterLogInService>.Instance, _authRepo.Object, _http.Object);

        private static IdentityProvider Provider(string? clientSecret = "secret-1") => new()
        {
            Provider = "twitter",
            ProviderType = "social",
            ClientId = "client-1",
            ClientSecret = clientSecret!,
            TokenEndpointAuthMethod = "client_secret_basic",
            TokenUrl = "https://twitter/token",
            UserInfoUrl = "https://twitter/userinfo",
            InitialRoles = new List<string> { "init-role" },
            InitialPermissions = new List<string> { "perm-1" }
        };

        private static StateInfo State() => new()
        {
            ClientId = "client-1",
            Provider = "twitter",
            Audience = "aud-1",
            Code = "auth-code",
            RedirectUri = "https://app/callback",
            Extra = new Dictionary<string, string> { { "code_verifier", "verifier-123" } }
        };

        private void SetupToken(TwitterOauthAccessToken? token, string error = "",
            Action<Dictionary<string, string>, Dictionary<string, string>?>? capture = null)
        {
            var setup = _http.Setup(h => h.SendFormUrlEncoded<TwitterOauthAccessToken>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()));

            if (capture != null)
            {
                setup.Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken, int?>(
                    (_, post, _, headers, _, _) => capture(post, headers));
            }

            setup.ReturnsAsync((token!, error));
        }

        private void SetupProfile(string? json, string error = "")
        {
            var doc = json == null ? null : JsonDocument.Parse(json);
            _http.Setup(h => h.Get<JsonDocument>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((doc!, error));
        }

        [Fact]
        public async Task ReturnsEmpty_WhenIdentityProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<TwitterUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenCodeVerifierMissing()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            var state = State();
            state.Extra = null;

            var result = await Create().HandleSocialLoginCallback(state);

            result.ExternalUserData.Should().BeOfType<TwitterUserData>();
            result.AccessToken.Should().BeNull();
            _http.Verify(h => h.SendFormUrlEncoded<TwitterOauthAccessToken>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()),
                Times.Never);
        }

        [Fact]
        public async Task ReturnsEmpty_WhenTokenExchangeFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "bad_request");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<TwitterUserData>();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenProfileFetchReturnsError()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new TwitterOauthAccessToken { AccessToken = "at-1" });
            SetupProfile(null, "profile boom");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<TwitterUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenProfileIsNullWithoutError()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new TwitterOauthAccessToken { AccessToken = "at-1" });
            SetupProfile(null, "");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<TwitterUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ConfidentialClient_MapsProfileAndUsesBasicAuthHeader()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider("secret-1"));
            Dictionary<string, string>? capturedPost = null;
            Dictionary<string, string>? capturedHeaders = null;
            SetupToken(new TwitterOauthAccessToken { AccessToken = "at-1", RefreshToken = "rt-1" },
                capture: (p, h) => { capturedPost = p; capturedHeaders = h; });
            SetupProfile("""
                {"data":{"id":"tw-1","name":"John Doe","confirmed_email":"john@x.com","username":"johnd","profile_image_url":"https://img/x.png"}}
                """);

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<TwitterUserData>().Subject;
            user.ExternalProviderUserId.Should().Be("tw-1");
            user.DisplayName.Should().Be("John Doe");
            user.FirstName.Should().Be("John");
            user.LastName.Should().Be("Doe");
            user.Email.Should().Be("john@x.com");
            user.UserName.Should().Be("johnd");
            user.ProfileImageUrl.Should().Be("https://img/x.png");
            user.Platform.Should().Be("twitter");
            user.Roles.Should().ContainSingle().Which.Should().Be("init-role");
            user.Permissions.Should().ContainSingle().Which.Should().Be("perm-1");
            result.AccessToken.Should().Be("at-1");
            result.RefreshToken.Should().Be("rt-1");

            // confidential client -> Basic auth header, no client_id in body
            capturedHeaders.Should().NotBeNull();
            capturedHeaders!.Should().ContainKey("Authorization");
            capturedHeaders["Authorization"].Should().StartWith("Basic ");
            capturedPost.Should().NotContainKey("client_id");
        }

        [Fact]
        public async Task PublicClient_PutsClientIdInBody_AndNoAuthHeader()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider(""));
            Dictionary<string, string>? capturedPost = null;
            Dictionary<string, string>? capturedHeaders = null;
            SetupToken(new TwitterOauthAccessToken { AccessToken = "at-1" },
                capture: (p, h) => { capturedPost = p; capturedHeaders = h; });
            SetupProfile("""
                {"data":{"id":"tw-2","name":"Solo","confirmed_email":"solo@x.com","username":"solo"}}
                """);

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<TwitterUserData>().Subject;
            user.ExternalProviderUserId.Should().Be("tw-2");
            // single-word name -> LastName empty, no profile image -> null
            user.FirstName.Should().Be("Solo");
            user.LastName.Should().BeEmpty();
            user.ProfileImageUrl.Should().BeNull();

            capturedHeaders.Should().BeNull();
            capturedPost.Should().ContainKey("client_id");
            capturedPost!["client_id"].Should().Be("client-1");
        }

        [Fact]
        public async Task HandleSocialLogin_ReturnsExternalUserData()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new TwitterOauthAccessToken { AccessToken = "at-1" });
            SetupProfile("""
                {"data":{"id":"tw-3","name":"A B C","confirmed_email":"abc@x.com","username":"abc"}}
                """);

            var user = await Create().HandleSocialLogin(State());

            var twitter = user.Should().BeOfType<TwitterUserData>().Subject;
            twitter.ExternalProviderUserId.Should().Be("tw-3");
            twitter.FirstName.Should().Be("A");
            twitter.LastName.Should().Be("C");
        }
    }
}
