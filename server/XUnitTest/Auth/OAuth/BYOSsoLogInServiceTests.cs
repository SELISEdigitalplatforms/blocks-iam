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
    public class BYOSsoLogInServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IHttpService> _http = new();
        private readonly Mock<IExternalUserMapperRegistry> _mapperRegistry = new();

        private BYOSsoLogInService Create() =>
            new(NullLogger<BYOSsoLogInService>.Instance, _authRepo.Object, _http.Object, _mapperRegistry.Object);

        private static IdentityProvider Provider() => new()
        {
            Provider = "okta",
            ProviderType = "enterprise",
            ClientId = "client-1",
            ClientSecret = "secret-1",
            TokenEndpointAuthMethod = "client_secret_post",
            TokenUrl = "https://okta/token",
            UserInfoUrl = "https://okta/userinfo",
            InitialRoles = new List<string> { "init-role" },
            InitialPermissions = new List<string> { "perm-1" }
        };

        private static StateInfo State() => new()
        {
            ClientId = "client-1",
            Provider = "okta",
            Audience = "aud-1",
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

        // Source calls _httpService.Get<dynamic>(...) which compiles to Get<object>(...).
        private void SetupUserInfo(object? payload, string error = "")
        {
            _http.Setup(h => h.Get<object>(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((payload!, error));
        }

        [Fact]
        public async Task ReturnsEmpty_WhenIdentityProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<BYOSsoUserData>();
            result.ExternalUserData.Email.Should().BeEmpty();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenTokenExchangeFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "invalid_grant");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<BYOSsoUserData>();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenUserInfoFetchFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });
            SetupUserInfo(null, "userinfo boom");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<BYOSsoUserData>();
            _mapperRegistry.Verify(m => m.Map(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<BYOSsoUserData>()),
                Times.Never);
        }

        // Characterization test documenting a latent production bug on the
        // SUCCESS path. BYOSsoLogInService calls _httpService.Get<dynamic>(...),
        // which the real HttpService (where T : class) deserializes via
        // JsonSerializer.Deserialize<object>(...) into a boxed JsonElement
        // (a value type). Line "result.Item1 == null" is a dynamic comparison
        // of that boxed struct against null, which the C# runtime binder cannot
        // resolve -> RuntimeBinderException. Because of this, the mapping branch
        // (mapperRegistry.Map) is UNREACHABLE without a production fix, so it
        // cannot be asserted green. We pin the actual behavior instead.
        [Fact]
        public async Task SuccessPath_Throws_BecauseDynamicUserInfoCannotBeNullChecked()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1" });

            // Matches what the real HttpService.Get<object> returns for a JSON body.
            var payload = JsonDocument.Parse("""{"email":"bring@your.own","sub":"byo-1"}""").RootElement.Clone();
            SetupUserInfo(payload);

            var act = async () => await Create().HandleSocialLoginCallback(State());

            (await act.Should().ThrowAsync<Exception>())
                .Which.Message.Should().Contain("JsonElement");

            // mapping is never reached
            _mapperRegistry.Verify(m => m.Map(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<BYOSsoUserData>()),
                Times.Never);
        }
    }
}
