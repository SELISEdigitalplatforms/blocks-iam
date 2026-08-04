using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace XUnitTest.Auth.OAuth
{
    public class RefreshTokenAuthenticationServiceTests : IDisposable
    {
        private readonly Mock<IJwtAccessTokenProvider> _jwtProvider = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _tokenManager = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();

        public RefreshTokenAuthenticationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        internal static Tenant MakeTenant(string id = "tenant-1") => new()
        {
            TenantId = id,
            Name = "t",
            DbConnectionString = string.Empty,
            Applications = new List<Applications>(),
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
        };

        internal static SigningCredentials RsaSigningCredentials()
        {
            var rsa = RSA.Create(2048);
            return new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        }

        internal static JwtAccessToken MakeJwtAccessToken() => new()
        {
            Issuer = "https://issuer",
            Audience = "aud",
            Claims = new List<Claim> { new(BlocksContext.SUBJECT_CLAIM, "blocks|u1") },
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = RsaSigningCredentials()
        };

        private static TokenRequest MakeRequest() => new()
        {
            GrantType = GrantTypes.RefreshToken,
            ClientId = "client-1",
            RefreshToken = "old-token",
            Request = new DefaultHttpContext().Request
        };

        private RefreshTokenAuthenticationService Create() =>
            new(NullLogger<RefreshTokenAuthenticationService>.Instance, _jwtProvider.Object, _tenants.Object, _tokenManager.Object, _authRepo.Object);

        [Fact]
        public async Task Authenticate_TenantNotFound_ReturnsServerError()
        {
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var result = await Create().AuthenticateAsync(MakeRequest(), new IdentityConfiguration(), new User { ItemId = "u1" });

            result.Error.Should().Be("server_error");
            result.ErrorDescription.Should().Be("Tenant not found");
            result.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task Authenticate_RefreshTokenGenerationFails_ReturnsInvalidRefreshToken()
        {
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "client-1", AllowedScopes = new List<string> { "openid" } });
            _jwtProvider.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(MakeJwtAccessToken());
            _tokenManager.Setup(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()))
                .ReturnsAsync((string.Empty, default(DateTime)));

            var result = await Create().AuthenticateAsync(MakeRequest(), new IdentityConfiguration(), new User { ItemId = "u1" });

            result.Error.Should().Be(OAuthError.InvalidRefreshToken);
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task Authenticate_Success_ReturnsSignedAccessTokenAndNewRefreshToken()
        {
            var expiry = DateTime.UtcNow.AddMinutes(60);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("client-1"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "client-1", AllowedScopes = new List<string> { "openid" } });
            _jwtProvider.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(MakeJwtAccessToken());
            _tokenManager.Setup(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()))
                .ReturnsAsync(("new-refresh-token", expiry));

            var config = new IdentityConfiguration { AccessTokenValidForNumberMinutes = 42 };
            var result = await Create().AuthenticateAsync(MakeRequest(), config, new User { ItemId = "u1" });

            result.Error.Should().BeNullOrEmpty();
            result.RefreshToken.Should().Be("new-refresh-token");
            result.RefreshExpiresUtc.Should().Be(expiry);
            result.ExpiresIn.Should().Be(42 * 60);
            result.AccessToken.Should().NotBeNullOrWhiteSpace();

            // Access token is a well-formed JWT carrying the subject claim.
            var parsed = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
            parsed.Claims.Should().Contain(c => c.Type == BlocksContext.SUBJECT_CLAIM && c.Value == "blocks|u1");

            _authRepo.Verify(r => r.GetOidcClientRegistrationAsync("client-1"), Times.Once);
            _tokenManager.Verify(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()), Times.Once);
        }
    }
}
