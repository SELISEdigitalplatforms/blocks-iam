using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Oidc;

public class TokenRevocationServiceTests
{
    private readonly Mock<ITokenRevocationRepository> _revocationRepoMock;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepoMock;
    private readonly Mock<IAuthenticationRepository> _authenticationRepositoryMock;
    private readonly Mock<ILogger<TokenRevocationService>> _loggerMock;
    private readonly TokenRevocationService _service;

    public TokenRevocationServiceTests()
    {
        _revocationRepoMock = new Mock<ITokenRevocationRepository>();
        _refreshTokenRepoMock = new Mock<IRefreshTokenRepository>();
        _authenticationRepositoryMock = new Mock<IAuthenticationRepository>();
        _loggerMock = new Mock<ILogger<TokenRevocationService>>();

        _service = new TokenRevocationService(
            _revocationRepoMock.Object,
            _refreshTokenRepoMock.Object,
            _authenticationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task RevokeTokenAsync_WithUnknownRefreshToken_ReturnsSuccess()
    {
        _refreshTokenRepoMock
            .Setup(x => x.GetByTokenIdAsync("unknown-refresh-token"))
            .ReturnsAsync((RefreshTokenModel)null!);

        var result = await _service.RevokeTokenAsync("unknown-refresh-token", "refresh_token", "client-a");

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        _refreshTokenRepoMock.Verify(x => x.RevokeByFamilyIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RevokeTokenAsync_WithRefreshTokenAndMismatchedClient_ReturnsInvalidClient()
    {
        _refreshTokenRepoMock
            .Setup(x => x.GetByTokenIdAsync("refresh-token-1"))
            .ReturnsAsync(new RefreshTokenModel
            {
                TokenId = "refresh-token-1",
                FamilyId = "family-1",
                UserId = "user-1",
                ClientId = "client-a",
                IsRevoked = false
            });

        var result = await _service.RevokeTokenAsync("refresh-token-1", "refresh_token", "client-b");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_client");
        _refreshTokenRepoMock.Verify(x => x.RevokeByFamilyIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RevokeTokenAsync_WithAccessTokenAndMismatchedClient_ReturnsInvalidClient()
    {
        var token = BuildJwt("jti-1", "user-1", "client-a");

        var result = await _service.RevokeTokenAsync(token, "access_token", "client-b");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_client");
        _revocationRepoMock.Verify(
            x => x.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()),
            Times.Never);
    }

    [Fact]
    public async Task IntrospectTokenAsync_WithMismatchedClient_ReturnsInactive()
    {
        var token = BuildJwt("jti-2", "user-2", "client-a");

        _revocationRepoMock
            .Setup(x => x.IsRevokedAsync("jti-2"))
            .ReturnsAsync(false);

        var result = await _service.IntrospectTokenAsync(token, "access_token", "client-b");

        result.Active.Should().BeFalse();
    }

    private static string BuildJwt(string jti, string sub, string aud)
    {
        var jwt = new JwtSecurityToken(
            issuer: "test-issuer",
            audience: aud,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Jti, jti),
                new Claim(JwtRegisteredClaimNames.Sub, sub),
                new Claim("scope", "openid profile")
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
