using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Authentication.DomainService.Oidc.Contracts;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class TokenRevocationServiceTests
    {
        private readonly Mock<ITokenRevocationRepository> _revocationRepo = new();
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();

        private TokenRevocationService Create() =>
            new(_revocationRepo.Object, _refreshRepo.Object, _authRepo.Object,
                NullLogger<TokenRevocationService>.Instance);

        /// <summary>Builds a signed JWT carrying jti, audience (client), subject, scope and expiry.</summary>
        private static string BuildJwt(string jti, string audience, string subject = "user-1",
            string scope = "openid profile", DateTime? expires = null, DateTime? issuedAt = null)
        {
            var now = issuedAt ?? DateTime.UtcNow;
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Jti, jti),
                new(JwtRegisteredClaimNames.Sub, subject),
                new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64),
                new("scope", scope)
            };
            var key = new SymmetricSecurityKey(new byte[32]);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: "https://idp.example.com",
                audience: audience,
                claims: claims,
                notBefore: now.AddMinutes(-1),
                expires: expires ?? now.AddMinutes(30),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ---- RevokeTokenAsync ----

        [Fact]
        public async Task Revoke_EmptyToken_ReturnsInvalidRequest()
        {
            var result = await Create().RevokeTokenAsync("", "", "client-1");
            result.Success.Should().BeFalse();
            result.Error.Should().Be("invalid_request");
        }

        [Fact]
        public async Task Revoke_RefreshToken_MatchingClient_RevokesAndSyncsSession()
        {
            var rt = new RefreshTokenModel { TokenId = "rt-1", ClientId = "client-1", IsRevoked = false };
            _refreshRepo.Setup(r => r.GetByTokenIdAsync("rt-1")).ReturnsAsync(rt);
            _refreshRepo.Setup(r => r.RevokeByTokenIdAsync("rt-1", "user_revoked")).ReturnsAsync(true);
            _refreshRepo.Setup(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "revoked")).ReturnsAsync(1);

            var result = await Create().RevokeTokenAsync("rt-1", "refresh_token", "client-1");

            result.Success.Should().BeTrue();
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync("rt-1", "user_revoked"), Times.Once);
            _refreshRepo.Verify(r => r.RevokeAllByTokenIdsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("rt-1")), "revoked"), Times.Once);
        }

        [Fact]
        public async Task Revoke_RefreshToken_WrongClient_ReturnsInvalidClient()
        {
            var rt = new RefreshTokenModel { TokenId = "rt-1", ClientId = "other-client", IsRevoked = false };
            _refreshRepo.Setup(r => r.GetByTokenIdAsync("rt-1")).ReturnsAsync(rt);

            var result = await Create().RevokeTokenAsync("rt-1", "refresh_token", "client-1");

            result.Success.Should().BeFalse();
            result.Error.Should().Be("invalid_client");
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Revoke_RefreshToken_AlreadyRevoked_ReturnsSuccess_WithoutRevoking()
        {
            var rt = new RefreshTokenModel { TokenId = "rt-1", ClientId = "client-1", IsRevoked = true };
            _refreshRepo.Setup(r => r.GetByTokenIdAsync("rt-1")).ReturnsAsync(rt);

            var result = await Create().RevokeTokenAsync("rt-1", "refresh_token", "client-1");

            result.Success.Should().BeTrue();
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Revoke_RefreshHint_UnknownToken_ReturnsSuccess()
        {
            _refreshRepo.Setup(r => r.GetByTokenIdAsync(It.IsAny<string>())).ReturnsAsync((RefreshTokenModel)null!);

            var result = await Create().RevokeTokenAsync("unknown", "refresh_token", "client-1");

            result.Success.Should().BeTrue();
        }

        [Fact]
        public async Task Revoke_AccessToken_MatchingAudience_AddsToBlacklist()
        {
            _refreshRepo.Setup(r => r.GetByTokenIdAsync(It.IsAny<string>())).ReturnsAsync((RefreshTokenModel)null!);
            var jwt = BuildJwt("jti-1", "client-1");
            _revocationRepo.Setup(r => r.RevokeTokenAsync("jti-1", "user-1", "user_revoked", It.IsAny<DateTime>())).ReturnsAsync(true);

            var result = await Create().RevokeTokenAsync(jwt, "access_token", "client-1");

            result.Success.Should().BeTrue();
            _revocationRepo.Verify(r => r.RevokeTokenAsync("jti-1", "user-1", "user_revoked", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Revoke_AccessToken_WrongAudience_ReturnsInvalidClient()
        {
            _refreshRepo.Setup(r => r.GetByTokenIdAsync(It.IsAny<string>())).ReturnsAsync((RefreshTokenModel)null!);
            var jwt = BuildJwt("jti-1", "some-other-client");

            var result = await Create().RevokeTokenAsync(jwt, "access_token", "client-1");

            result.Success.Should().BeFalse();
            result.Error.Should().Be("invalid_client");
            _revocationRepo.Verify(r => r.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task Revoke_GarbageToken_WithoutHint_ReturnsSuccess()
        {
            _refreshRepo.Setup(r => r.GetByTokenIdAsync(It.IsAny<string>())).ReturnsAsync((RefreshTokenModel)null!);

            var result = await Create().RevokeTokenAsync("not-a-jwt", "", "client-1");

            result.Success.Should().BeTrue();
        }

        // ---- IntrospectTokenAsync ----

        [Fact]
        public async Task Introspect_EmptyToken_ReturnsInvalidRequest()
        {
            var result = await Create().IntrospectTokenAsync("", "", "client-1");
            result.Active.Should().BeFalse();
            result.Error.Should().Be("invalid_request");
        }

        [Fact]
        public async Task Introspect_GarbageToken_ReturnsInactive()
        {
            var result = await Create().IntrospectTokenAsync("not-a-jwt", "", "client-1");
            result.Active.Should().BeFalse();
        }

        [Fact]
        public async Task Introspect_RevokedToken_ReturnsInactive()
        {
            var jwt = BuildJwt("jti-2", "client-1");
            _revocationRepo.Setup(r => r.IsRevokedAsync("jti-2")).ReturnsAsync(true);

            var result = await Create().IntrospectTokenAsync(jwt, "access_token", "client-1");

            result.Active.Should().BeFalse();
        }

        [Fact]
        public async Task Introspect_WrongClient_ReturnsInactive()
        {
            var jwt = BuildJwt("jti-3", "other-client");
            _revocationRepo.Setup(r => r.IsRevokedAsync("jti-3")).ReturnsAsync(false);

            var result = await Create().IntrospectTokenAsync(jwt, "access_token", "client-1");

            result.Active.Should().BeFalse();
        }

        [Fact]
        public async Task Introspect_ExpiredToken_ReturnsInactive_WithExp()
        {
            var jwt = BuildJwt("jti-4", "client-1", expires: DateTime.UtcNow.AddMinutes(-5), issuedAt: DateTime.UtcNow.AddMinutes(-10));
            _revocationRepo.Setup(r => r.IsRevokedAsync("jti-4")).ReturnsAsync(false);

            var result = await Create().IntrospectTokenAsync(jwt, "access_token", "client-1");

            result.Active.Should().BeFalse();
            result.Exp.Should().NotBeNull();
        }

        [Fact]
        public async Task Introspect_ValidToken_ReturnsActive_WithClaims()
        {
            var jwt = BuildJwt("jti-5", "client-1", subject: "user-9", scope: "openid email");
            _revocationRepo.Setup(r => r.IsRevokedAsync("jti-5")).ReturnsAsync(false);

            var result = await Create().IntrospectTokenAsync(jwt, "access_token", "client-1");

            result.Active.Should().BeTrue();
            result.Jti.Should().Be("jti-5");
            result.Sub.Should().Be("user-9");
            result.ClientId.Should().Be("client-1");
            result.Scope.Should().Be("openid email");
            result.TokenType.Should().Be("Bearer");
        }

        // ---- Bulk revocation ----

        [Fact]
        public async Task RevokeAllUserTokens_RevokesActiveTokens_AndSyncsSessions()
        {
            var tokens = new List<RefreshTokenModel>
            {
                new() { TokenId = "t1", IsRevoked = false },
                new() { TokenId = "t2", IsRevoked = true },
                new() { TokenId = "t3", IsRevoked = false },
            };
            _refreshRepo.Setup(r => r.GetByUserAsync("user-1", "tenant-1")).ReturnsAsync(tokens);
            _refreshRepo.Setup(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), "logout")).ReturnsAsync(true);
            _refreshRepo.Setup(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "revoked")).ReturnsAsync(3);

            var ok = await Create().RevokeAllUserTokensAsync("user-1", "tenant-1", "logout");

            ok.Should().BeTrue();
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync("t1", "logout"), Times.Once);
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync("t3", "logout"), Times.Once);
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync("t2", "logout"), Times.Never);
        }

        [Fact]
        public async Task RevokeUserClientTokens_OnlyRevokesMatchingClient()
        {
            var tokens = new List<RefreshTokenModel>
            {
                new() { TokenId = "t1", ClientId = "client-1", IsRevoked = false },
                new() { TokenId = "t2", ClientId = "client-2", IsRevoked = false },
            };
            _refreshRepo.Setup(r => r.GetByUserAsync("user-1", "")).ReturnsAsync(tokens);
            _refreshRepo.Setup(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), "reason")).ReturnsAsync(true);
            _refreshRepo.Setup(r => r.RevokeAllByTokenIdsAsync(It.IsAny<IEnumerable<string>>(), "revoked")).ReturnsAsync(1);

            var ok = await Create().RevokeUserClientTokensAsync("user-1", "client-1", "reason");

            ok.Should().BeTrue();
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync("t1", "reason"), Times.Once);
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync("t2", "reason"), Times.Never);
        }

        [Fact]
        public async Task GetRevocationHistory_DelegatesToRepository()
        {
            var history = new List<TokenRevocationModel> { new() { Jti = "j1" } };
            _revocationRepo.Setup(r => r.GetRevokedTokensByUserAsync("user-1")).ReturnsAsync(history);

            var result = await Create().GetRevocationHistoryAsync("user-1");

            result.Should().BeEquivalentTo(history);
        }
    }
}
