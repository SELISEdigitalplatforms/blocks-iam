using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.Oidc;

/// <summary>
/// iam SPEC2 end to end on the local throwaway Redis + Mongo: real code store, real refresh-token
/// repository (MongoDB + Redis refresh cache), real jti revocation list and the real refresh resolver.
/// Only token minting is faked; it saves the refresh token to MongoDB and the Redis cache the way
/// <c>UnifiedTokenSessionService.CreateOrRotateRefreshToken</c> does.
/// </summary>
public sealed class AuthorizationCodeReuseIntegrationTests : IDisposable
{
    private const string ClientId = "cid";
    private const string RedirectUri = "https://app/cb";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("iam-spec2-test-signing-key-0123456789abcdef"));

    private readonly RealRedis _redis = new();
    private readonly RealMongo? _mongo = RealMongo.IsAvailable ? new RealMongo() : null;
    private readonly string _tenant;
    private readonly List<string> _refreshKeys = new();
    private readonly Mock<ITokenGenerationService> _tokens = new();
    private readonly Mock<IAuthenticationRepository> _auth = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAuthorizationClaimsResolver> _claims = new();
    private readonly Mock<ITenants> _tenants = new();

    private ICacheClient _cache = null!;
    private RedisAuthorizationCodeStore _codes = null!;
    private RefreshTokenRepository _refresh = null!;
    private TokenRevocationRepository _revocations = null!;
    private Func<Task>? _duringMint;

    public AuthorizationCodeReuseIntegrationTests()
    {
        _tenant = _redis.NewTenant();
        BlocksContext.IsTestMode = true;
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: _tenant, roles: null, userId: null, impersonated: false,
            isAuthenticated: false, requestUri: "https://test", organizationId: "default",
            permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: null,
            userName: null, phoneNumber: null, displayName: null, oauthToken: null,
            originalTenantId: _tenant, impersonationSessionId: null, applicationDomain: "test"));

        if (!RealRedis.IsAvailable || _mongo == null)
        {
            return;
        }

        _cache = _redis.CacheClient();
        var provider = _mongo.ProviderFor();
        _codes = new RedisAuthorizationCodeStore(_cache, new AuthorizationCodeRepository(provider), NullLogger<RedisAuthorizationCodeStore>.Instance);
        _refresh = new RefreshTokenRepository(provider, _cache, NullLogger<RefreshTokenRepository>.Instance, new XUnitTest.TestDoubles.PassThroughIamDataAccess());
        _revocations = new TokenRevocationRepository(provider, NullLogger<TokenRevocationRepository>.Instance, new XUnitTest.TestDoubles.PassThroughIamDataAccess());

        _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
        _auth.Setup(a => a.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
        _auth.Setup(a => a.GetOidcClientRegistrationAsync(ClientId))
            .ReturnsAsync(new OidcClientRegistration { ItemId = ClientId, ClientId = ClientId, UseTokensCookie = false });
        _users.Setup(u => u.GetUserByIdAsync("user-1")).ReturnsAsync(new User { ItemId = "user-1", FirstName = "J", Email = "j@x.com" });
        _claims.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new ResolvedAuthorizationClaims());
        _tokens.Setup(t => t.GenerateIdTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync("id-token");
        _tokens.Setup(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(() => AccessToken());
        _tokens.Setup(t => t.GenerateRefreshTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Returns<OidcClaims, string, bool, string>(async (claims, _, _, sid) =>
            {
                var model = await SaveRefreshTokenAsync(claims.Sub, sid, lineage: null);
                if (_duringMint is { } hook)
                {
                    _duringMint = null;
                    await hook();
                }
                return model;
            });
    }

    public void Dispose()
    {
        foreach (var key in _refreshKeys)
        {
            _redis.Database.KeyDelete(key);
        }
        _redis.Dispose();
        _mongo?.Dispose();
        BlocksContext.SetContext(null);
    }

    // ---------------- helpers ----------------

    private static string AccessToken() =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iam", audience: "apis",
            claims: new[] { new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")), new Claim("sub", "user-1") },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)));

    /// <summary>Like CreateOrRotateRefreshToken: Mongo row + Redis cache entry under the raw TokenId.</summary>
    private async Task<RefreshTokenModel> SaveRefreshTokenAsync(string userId, string sessionId, string? lineage)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid().ToString("n");
        _refreshKeys.Add(id);
        var model = new RefreshTokenModel
        {
            TokenId = id, UserId = userId, TenantId = _tenant, ClientId = ClientId, SessionId = sessionId,
            RefreshTokenSessionId = lineage ?? id, IssuedUtc = now,
            SlidingExpiry = now.AddMinutes(30), AbsoluteExpiry = now.AddDays(1)
        };
        await _cache.AddStringValueAsync(id, JsonSerializer.Serialize(new RefreshTokenCache
        {
            RefreshToken = id, TenantId = _tenant, ClientId = ClientId, SessionId = sessionId,
            RefreshTokenSessionId = model.RefreshTokenSessionId, UserId = userId, IssuedUtc = now,
            ExpiresUtc = model.SlidingExpiry, AbsoluteExpiresUtc = model.AbsoluteExpiry
        }), 1800);
        await _refresh.CreateAsync(model);
        return model;
    }

    private AuthorizationCodeExchangeService Exchange() =>
        new(_codes, _refresh, _tokens.Object, Mock.Of<IPkceService>(), _users.Object, _claims.Object,
            _auth.Object, XUnitTest.TestDoubles.OidcClientSecretTestDoubles.Resolver(), _tenants.Object, _revocations, _cache, NullLogger<AuthorizationCodeExchangeService>.Instance);

    private RefreshSessionResolver Resolver() => new(_cache, _refresh, NullLogger<RefreshSessionResolver>.Instance);

    private static IdentityConfiguration Config() => new()
    {
        RefreshTokenValidForNumberMinutes = 30, AbsoluteRefreshTokenValidForNumberMinutes = 1440, TokenRotationGracePeriodMinutes = 0
    };

    private async Task<string> NewCodeAsync()
    {
        var code = "code-" + Guid.NewGuid().ToString("N");
        await _codes.CreateAsync(new AuthorizationCodeModel
        {
            Code = code, ClientId = ClientId, TenantId = _tenant, UserId = "user-1", RedirectUri = RedirectUri,
            Scope = "openid", IdpSessionId = "idp-1", ExpiresAt = DateTime.UtcNow.AddMinutes(10), CreatedAt = DateTime.UtcNow
        });
        return code;
    }

    private static HttpRequest Request(string code)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["code"] = code, ["client_id"] = ClientId, ["redirect_uri"] = RedirectUri
        });
        return ctx.Request;
    }

    private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

    private async Task<(string Refresh, string Access)> SignInAsync()
    {
        var result = await Exchange().ExchangeAsync(Request(await NewCodeAsync()));
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ((string)Prop(ok.Value, "refresh_token")!, (string)Prop(ok.Value, "access_token")!);
    }

    private Task<RefreshTokenModel> MongoToken(string id) => _refresh.GetByTokenIdAsync(id);

    // ---------------- tests ----------------

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task Reuse_RevokesAndEvictsTheFirstRefreshToken_RevokesTheJti_LaterSignInKeepsRefreshing()
    {
        var code = await NewCodeAsync();
        var first = await Exchange().ExchangeAsync(Request(code));
        var firstOk = first.Should().BeOfType<OkObjectResult>().Subject;
        var refresh1 = (string)Prop(firstOk.Value, "refresh_token")!;
        var access1 = (string)Prop(firstOk.Value, "access_token")!;
        var jti1 = new JwtSecurityTokenHandler().ReadJwtToken(access1).Id;
        (await _redis.Database.KeyExistsAsync(refresh1)).Should().BeTrue("the refresh cache holds the token from creation");
        (await Resolver().TryResolveRefreshSessionAsync(refresh1, Config())).Should().NotBeNull("cache hit before the reuse");
        var minted = JsonSerializer.Deserialize<AuthorizationCodeMint>(
            (string)(await _redis.Database.HashGetAsync(IamRedisKeys.AuthorizationCode(_tenant, code), "minted"))!);
        minted!.LineageId.Should().Be(refresh1);
        minted.Jti.Should().Be(jti1);

        var again = await Exchange().ExchangeAsync(Request(code));

        var bad = again.Should().BeOfType<BadRequestObjectResult>().Subject;
        Prop(bad.Value, "error").Should().Be("invalid_grant");
        Prop(bad.Value, "error_description").Should().Be("Authorization code is invalid or expired");
        (await _redis.Database.KeyExistsAsync(refresh1)).Should().BeFalse("the reuse evicts the cached refresh token");
        var row = await MongoToken(refresh1);
        row.IsRevoked.Should().BeTrue();
        row.RevokeReason.Should().Be("authorization_code_reuse");
        (await Resolver().TryResolveRefreshSessionAsync(refresh1, Config())).Should().BeNull("refresh with the first token = invalid_grant");
        (await _revocations.IsRevokedAsync(jti1)).Should().BeTrue("introspection reports the first access token inactive");

        var (laterRefresh, _) = await SignInAsync();
        (await Resolver().TryResolveRefreshSessionAsync(laterRefresh, Config())).Should().NotBeNull("a later sign-in keeps refreshing");
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task ReuseOfAnotherCode_DoesNotTouchThisSignIn()
    {
        var (mine, _) = await SignInAsync();
        var other = await NewCodeAsync();
        await Exchange().ExchangeAsync(Request(other));
        await Exchange().ExchangeAsync(Request(other));

        (await Resolver().TryResolveRefreshSessionAsync(mine, Config())).Should().NotBeNull();
        (await MongoToken(mine)).IsRevoked.Should().BeFalse();
        (await _redis.Database.KeyExistsAsync(mine)).Should().BeTrue();
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task Reuse_AfterTheFirstTokenWasRotated_EvictsAndRevokesTheNewestToo()
    {
        var code = await NewCodeAsync();
        var first = await Exchange().ExchangeAsync(Request(code));
        var refresh1 = (string)Prop(((OkObjectResult)first).Value, "refresh_token")!;
        // A rotation as today: successor saved (Mongo + cache), predecessor evicted and revoked.
        var rotated = await SaveRefreshTokenAsync("user-1", "idp-1", lineage: refresh1);
        await _cache.RemoveKeyAsync(refresh1);
        await _refresh.RevokeByTokenIdAsync(refresh1, "superseded_by_rotation", rotated.TokenId);
        (await Resolver().TryResolveRefreshSessionAsync(rotated.TokenId, Config())).Should().NotBeNull();

        await Exchange().ExchangeAsync(Request(code));

        (await _redis.Database.KeyExistsAsync(rotated.TokenId)).Should().BeFalse();
        (await MongoToken(rotated.TokenId)).RevokeReason.Should().Be("authorization_code_reuse");
        (await Resolver().TryResolveRefreshSessionAsync(rotated.TokenId, Config())).Should().BeNull();
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task ReuseWhileTheFirstExchangeIsMinting_RevokesTheFirstExchangesTokens_NoCookie_NoTokens()
    {
        var code = await NewCodeAsync();
        IActionResult? second = null;
        _duringMint = async () => second = await Exchange().ExchangeAsync(Request(code));
        var request = Request(code);

        var first = await Exchange().ExchangeAsync(request);

        Prop(second.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("invalid_grant");
        Prop(first.Should().BeOfType<BadRequestObjectResult>().Subject.Value, "error").Should().Be("invalid_grant");
        request.HttpContext.Response.Headers.Should().NotContainKey("Set-Cookie");
        var minted = _refreshKeys.Single();
        (await _redis.Database.KeyExistsAsync(minted)).Should().BeFalse();
        (await MongoToken(minted)).RevokeReason.Should().Be("authorization_code_reuse");
        (await _mongo!.Database.GetCollection<TokenRevocationModel>("IdpRevokedTokens")
            .CountDocumentsAsync(t => t.RevokeReason == "authorization_code_reuse")).Should().Be(1);
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task KnownLimit_TheAccessTokenOfAReusedCodeStillValidates_UntilItExpires()
    {
        // Genesis APIs validate the JWT (signature + lifetime) and never read IdpRevokedTokens; only
        // oidc/introspect sees the revoke (owner accepted, SPEC2 Q2). If this starts failing, the limit changed.
        var code = await NewCodeAsync();
        var first = await Exchange().ExchangeAsync(Request(code));
        var access = (string)Prop(((OkObjectResult)first).Value, "access_token")!;
        await Exchange().ExchangeAsync(Request(code));

        new JwtSecurityTokenHandler().ValidateToken(access, new TokenValidationParameters
        {
            ValidIssuer = "iam", ValidAudience = "apis", IssuerSigningKey = SigningKey
        }, out _).Should().NotBeNull();
        (await _revocations.IsRevokedAsync(new JwtSecurityTokenHandler().ReadJwtToken(access).Id)).Should().BeTrue();
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task TwentyParallelExchanges_OfOneCode_ExactlyOneGetsTokens()
    {
        var code = await NewCodeAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Exchange().ExchangeAsync(Request(code)))));

        results.Count(r => r is OkObjectResult).Should().BeLessThanOrEqualTo(1);
        results.Count(r => r is BadRequestObjectResult b && (string?)Prop(b.Value, "error") == "invalid_grant")
            .Should().BeGreaterThanOrEqualTo(19);
        _tokens.Verify(t => t.GenerateAccessTokenAsync(It.IsAny<OidcClaims>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task LateExchange_NearTheEndOfTheCodeLifetime_StillGetsTokens()
    {
        var code = "code-" + Guid.NewGuid().ToString("N");
        await _codes.CreateAsync(new AuthorizationCodeModel
        {
            Code = code, ClientId = ClientId, TenantId = _tenant, UserId = "user-1", RedirectUri = RedirectUri,
            Scope = "openid", IdpSessionId = "idp-1", ExpiresAt = DateTime.UtcNow.AddSeconds(30), CreatedAt = DateTime.UtcNow.AddSeconds(-570)
        });

        (await Exchange().ExchangeAsync(Request(code))).Should().BeOfType<OkObjectResult>();
        (await _redis.Database.KeyTimeToLiveAsync(IamRedisKeys.AuthorizationCode(_tenant, code)))!.Value
            .Should().BeGreaterThan(TimeSpan.FromSeconds(60), "the key outlives the code's own expiry by a minute");
    }
}
