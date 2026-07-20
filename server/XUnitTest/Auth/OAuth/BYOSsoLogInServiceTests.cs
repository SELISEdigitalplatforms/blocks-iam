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

        // Source calls _httpService.Get<JsonDocument>(...); the real HttpService
        // deserializes the JSON user-info body into a JsonDocument whose RootElement
        // is the JsonElement passed to the mapper registry.
        private void SetupUserInfo(JsonDocument? payload, string error = "")
        {
            _http.Setup(h => h.Get<JsonDocument>(
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

        // SUCCESS path. After the fix, BYOSsoLogInService calls
        // _httpService.Get<JsonDocument>(...) (JsonElement is a value type and
        // cannot satisfy the IHttpService.Get<T> "where T : class" constraint),
        // then hands RootElement to the mapper registry. The user-info payload is
        // now mapped onto the external user and returned with the provider tokens,
        // roles and permissions.
        [Fact]
        public async Task SuccessPath_MapsExternalUser_FromUserInfoPayload()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", IdToken = "id-1", RefreshToken = "rt-1" });

            // Matches what the real HttpService.Get<JsonDocument> returns for a JSON body.
            using var payload = JsonDocument.Parse("""{"email":"bring@your.own","sub":"byo-1"}""");
            SetupUserInfo(payload);

            // The mapper projects the raw JsonElement onto the strongly-typed user.
            _mapperRegistry
                .Setup(m => m.Map(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<BYOSsoUserData>()))
                .Callback<string, JsonElement, BYOSsoUserData>((_, json, user) =>
                {
                    user.Email = json.GetProperty("email").GetString()!;
                    user.ExternalProviderUserId = json.GetProperty("sub").GetString()!;
                });

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<BYOSsoUserData>().Subject;
            user.Email.Should().Be("bring@your.own");
            user.ExternalProviderUserId.Should().Be("byo-1");
            user.Platform.Should().Be("okta");
            user.Permissions.Should().BeEquivalentTo(new[] { "perm-1" });
            user.Roles.Should().BeEquivalentTo(new[] { "init-role" });

            result.AccessToken.Should().Be("at-1");
            result.IdToken.Should().Be("id-1");
            result.RefreshToken.Should().Be("rt-1");

            // Mapping is reached exactly once, with the provider name and the payload's root element.
            _mapperRegistry.Verify(
                m => m.Map("okta", It.Is<JsonElement>(e => e.GetProperty("email").GetString() == "bring@your.own"),
                    It.IsAny<BYOSsoUserData>()),
                Times.Once);
        }
    }
}
