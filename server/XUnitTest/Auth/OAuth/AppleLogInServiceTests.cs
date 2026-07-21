using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.SocialServices;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class AppleLogInServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IHttpService> _http = new();

        private static readonly string EcPrivateKeyPem = CreateEcPrivateKeyPem();

        private AppleLogInService Create() =>
            new(NullLogger<AppleLogInService>.Instance, _authRepo.Object, _cache.Object, _http.Object);

        private static IdentityProvider Provider(bool withKey = true) => new()
        {
            Provider = "apple",
            ProviderType = "social",
            ClientId = "com.example.app",
            ClientSecret = "unused-here",
            TokenEndpointAuthMethod = "client_secret_post",
            TokenUrl = "https://appleid.apple.com/auth/token",
            InitialRoles = new List<string> { "init-role" },
            InitialPermissions = new List<string> { "perm-1" },
            TeamId = "TEAM123456",
            KeyId = "KEY1234567",
            PrivateKey = withKey ? EcPrivateKeyPem : null,
            AppleAudience = "https://appleid.apple.com"
        };

        private static StateInfo State() => new()
        {
            ClientId = "com.example.app",
            Provider = "apple",
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

        // ---------- GenerateClientSecret ----------

        [Fact]
        public void GenerateClientSecret_ProducesSignedJwt_WithExpectedClaims()
        {
            var secret = Create().GenerateClientSecret(Provider());

            var token = new JwtSecurityTokenHandler().ReadJwtToken(secret);
            token.Header.Alg.Should().Be(SecurityAlgorithms.EcdsaSha256);
            token.Header.Kid.Should().Be("KEY1234567");
            token.Payload.Iss.Should().Be("TEAM123456");
            token.Payload.Sub.Should().Be("com.example.app");
            token.Audiences.Should().Contain("https://appleid.apple.com");
        }

        // ---------- HandleSocialLoginCallback ----------

        [Fact]
        public async Task ReturnsEmpty_WhenIdentityProviderNotFound()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>()))
                .ReturnsAsync((IdentityProvider)null!);

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<AppleUserData>();
            result.ExternalUserData.Email.Should().BeNull();
        }

        [Fact]
        public async Task ReturnsEmpty_WhenTokenExchangeFails()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            SetupToken(null, "invalid_client");

            var result = await Create().HandleSocialLoginCallback(State());

            result.ExternalUserData.Should().BeOfType<AppleUserData>();
            result.AccessToken.Should().BeNull();
        }

        [Fact]
        public async Task MapsUserFromIdToken_OnSuccess()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            var idToken = CreateJwt(("email", "apple@user.com"), ("sub", "apple-sub-1"));
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", RefreshToken = "rt-1", IdToken = idToken });

            var result = await Create().HandleSocialLoginCallback(State());

            var user = result.ExternalUserData.Should().BeOfType<AppleUserData>().Subject;
            user.Email.Should().Be("apple@user.com");
            user.ExternalProviderUserId.Should().Be("apple-sub-1");
            user.Platform.Should().Be("apple");
            user.Roles.Should().ContainSingle().Which.Should().Be("init-role");
            result.AccessToken.Should().Be("at-1");
            result.RefreshToken.Should().Be("rt-1");
        }

        [Fact]
        public async Task HandleSocialLogin_ReturnsExternalUserData()
        {
            _authRepo.Setup(r => r.GetIdentityProviderByClientIdAsync(It.IsAny<string>())).ReturnsAsync(Provider());
            var idToken = CreateJwt(("email", "a@b.com"), ("sub", "apple-sub-2"));
            SetupToken(new SocialOauthAccessToken { AccessToken = "at-1", IdToken = idToken });

            var user = await Create().HandleSocialLogin(State());

            user.Should().BeOfType<AppleUserData>();
            user.ExternalProviderUserId.Should().Be("apple-sub-2");
        }

        private static string CreateJwt(params (string Key, string Value)[] claims)
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                "test-key-with-sufficient-length-for-hmacsha256-algorithm"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwtClaims = claims.Select(c => new Claim(c.Key, c.Value)).ToList();
            var token = new JwtSecurityToken(
                issuer: "https://appleid.apple.com", audience: "com.example.app", claims: jwtClaims,
                expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
            return handler.WriteToken(token);
        }

        private static string CreateEcPrivateKeyPem()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return ecdsa.ExportECPrivateKeyPem();
        }
    }
}
