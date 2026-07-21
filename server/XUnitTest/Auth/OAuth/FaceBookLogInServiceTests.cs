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
    public class FaceBookLogInServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IHttpService> _http = new();

        private FaceBookLogInService Create() =>
            new(NullLogger<FaceBookLogInService>.Instance, _authRepo.Object, _http.Object);

        private static IdentityProvider Provider() => new()
        {
            Provider = "facebook",
            ProviderType = "social",
            ClientId = "client-1",
            ClientSecret = "secret-1",
            TokenEndpointAuthMethod = "client_secret_post",
            TokenUrl = "https://graph.facebook.com/oauth/access_token",
            UserInfoUrl = "https://graph.facebook.com/me",
            InitialRoles = new List<string> { "init-role" },
            InitialPermissions = new List<string> { "perm-1" }
        };

        private static StateInfo State() => new()
        {
            ClientId = "client-1",
            Provider = "facebook",
            Audience = "aud-1",
            Code = "auth-code",
            RedirectUri = "https://app/callback"
        };

        private void SetupToken(SocialOauthAccessToken? token, string error = "")
        {
            _http.Setup(h => h.Get<SocialOauthAccessToken>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((token!, error));
        }

        private void SetupProfile(FaceBookUserData? user, string error = "")
        {
            _http.Setup(h => h.Get<FaceBookUserData>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((user!, error));
        }

        [Fact]
        public async Task ReturnsEmpty_WhenIdentityProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<FaceBookUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenTokenFetchFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "token boom");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<FaceBookUserData>();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenProfileFetchReturnsError()
        {
            _authRepo.Setup(r => r.GetIdentityProviderAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupProfile(null, "profile boom");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<FaceBookUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task MapsProfile_OnSuccess()
        {
            _authRepo.Setup(r => r.GetIdentityProviderAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", RefreshToken = "rt-1", IdToken = "it-1" });
            SetupProfile(new FaceBookUserData
            {
                ExternalProviderUserId = "fb-1",
                DisplayName = "Face Book",
                Email = "fb@x.com"
            });

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<FaceBookUserData>().Subject;
            user.ExternalProviderUserId.Should().Be("fb-1");
            user.DisplayName.Should().Be("Face Book");
            user.Email.Should().Be("fb@x.com");
            result.AccessToken.Should().Be("at-1");
            result.RefreshToken.Should().Be("rt-1");
            result.IdToken.Should().Be("it-1");
        }

        [Fact]
        public async Task HandleSocialLogin_ReturnsExternalUserData()
        {
            _authRepo.Setup(r => r.GetIdentityProviderAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupProfile(new FaceBookUserData { ExternalProviderUserId = "fb-2", Email = "e@x.com" });

            var user = await Create().HandleSocialLogin(State());

            user.Should().BeOfType<FaceBookUserData>();
            user.ExternalProviderUserId.Should().Be("fb-2");
        }
    }
}
