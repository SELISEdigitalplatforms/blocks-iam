using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// Validation order and identity sourcing for the delegation token exchange.
    /// <para>
    /// The recurring theme: authority comes from the Redis record plus the live tenant DB, never
    /// from anything the caller can influence.
    /// </para>
    /// </summary>
    public class TokenExchangeAuthorizationServiceTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string TenantSalt = "tenant-salt-value";
        private const string UserId = "user-1";
        private const string OrganizationId = "org-1";
        private const string CertPassword = "pwd";

        private static readonly string GrantId = "dg_" + new string('a', 64);

        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IDatabase> _cacheDb = new();
        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly Mock<IJwtAccessTokenProvider> _jwtAccessTokenProvider = new();

        private readonly bool _originalTestMode = BlocksContext.IsTestMode;

        public TokenExchangeAuthorizationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            SetTenantContext(TenantId);

            _cache.Setup(c => c.CacheDatabase()).Returns(_cacheDb.Object);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(Tenant());

            // Default: the nonce is fresh, the rate counter is well inside its cap.
            AllowNonce();
            _cacheDb
                .Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1L);
            _cacheDb
                .Setup(db => db.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            StoreGrant(Record());
            _userRepository.Setup(r => r.GetUserByIdAsync(UserId)).ReturnsAsync(DelegatedUser());
            WireTokenMinting();
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = _originalTestMode;
        }

        // ------------------------------------------------------------------ fixtures

        private static void SetTenantContext(string? tenantId)
        {
            // On the anonymous token endpoint, BlocksContext carries the tenant only -- exactly what
            // TenantValidationMiddleware puts there from x-blocks-key.
            BlocksContext.SetContext(tenantId is null
                ? null
                : BlocksContext.Create(
                    tenantId: tenantId, roles: null, userId: null, isAuthenticated: false,
                    requestUri: null, organizationId: null, expireOn: DateTime.MinValue,
                    email: null, permissions: null, userName: null, phoneNumber: null,
                    displayName: null, oauthToken: null, originalTenantId: tenantId));
        }

        private static Tenant Tenant() => new()
        {
            TenantId = TenantId,
            ItemId = "tenant-item-1",
            TenantSalt = TenantSalt,
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters
            {
                Issuer = "https://issuer",
                PrivateCertificatePassword = CertPassword,
                IssueDate = DateTime.UtcNow
            }
        };

        private static DelegationGrantRecord Record(
            string tenantId = TenantId,
            string userId = UserId,
            string organizationId = OrganizationId,
            string tokenVersion = "3",
            string securityStamp = "stamp-3") => new()
            {
                TenantId = tenantId,
                UserId = userId,
                OrganizationId = organizationId,
                TokenVersion = tokenVersion,
                SecurityStamp = securityStamp
            };

        private static User DelegatedUser(
            bool active = true,
            int tokenVersion = 3,
            string securityStamp = "stamp-3") => new()
            {
                ItemId = UserId,
                Active = true is var _ && active,
                TokenVersion = tokenVersion,
                SecurityStamp = securityStamp,
                OrganizationIds = [OrganizationId],
                Roles = new Dictionary<string, List<string>> { [OrganizationId] = ["admin"] },
                Permissions = new Dictionary<string, List<string>> { [OrganizationId] = ["orders:write"] }
            };

        private void StoreGrant(DelegationGrantRecord? record)
        {
            _cache
                .Setup(c => c.GetStringValueAsync(DelegationPolicy.GrantKey(GrantId)))
                .ReturnsAsync(record is null ? null! : JsonSerializer.Serialize(record));
        }

        private void AllowNonce()
        {
            _cacheDb
                .Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(), When.NotExists, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
        }

        private void RejectNonceAsReplay()
        {
            _cacheDb
                .Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(), When.NotExists, It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);
        }

        private static byte[] GenerateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=blocks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return certificate.Export(X509ContentType.Pkcs12, CertPassword);
        }

        /// <summary>
        /// Mints a real signed JWT through the same claim-building path production uses, so the
        /// resulting token can be inspected claim by claim.
        /// </summary>
        private void WireTokenMinting()
        {
            var certificate = GenerateCertificate();

            _jwtAccessTokenProvider
                .Setup(p => p.GetJwtAccessToken(
                    It.IsAny<IdentityConfiguration>(),
                    It.IsAny<Tenant>(),
                    It.IsAny<User>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<StateInfo?>()))
                .ReturnsAsync((IdentityConfiguration config, Tenant tenant, User user, TokenRequest tokenRequest, StateInfo? _) =>
                {
                    var resolver = new AuthorizationClaimsResolver(new Mock<IUserRepository>().Object);
                    var resolved = resolver.ResolveAsync(user, tokenRequest.OrganizationId).GetAwaiter().GetResult();

                    var claimsIdentity = new ClaimsIdentity("seliseblocks-authentication");
                    JwtAccessTokenProvider.AddClaims(claimsIdentity, tenant, user, resolved, tokenRequest);

                    return new JwtAccessToken
                    {
                        AccessTokenValidForNumberMinute = 5,
                        Issuer = tenant.JwtTokenParameters.Issuer,
                        Audience = "blocks",
                        NotBefore = DateTime.UtcNow,
                        Expires = DateTime.UtcNow.AddMinutes(5),
                        Claims = claimsIdentity.Claims,
                        SigningCredentials = JwtAccessTokenProvider.MakeSigningCredentials(certificate, CertPassword)
                    };
                });
        }

        private TokenExchangeAuthorizationService Create() => new(
            _tenants.Object,
            _cache.Object,
            _userRepository.Object,
            _jwtAccessTokenProvider.Object,
            NullLogger<TokenExchangeAuthorizationService>.Instance);

        private static TokenRequest Request(
            string? subjectToken = null,
            string? subjectTokenType = null,
            string? nonce = "0f1e2d3c4b5a69788796a5b4c3d2e1f0",
            long? ts = null,
            string? signature = null,
            string tenantIdForSignature = TenantId,
            string saltForSignature = TenantSalt)
        {
            subjectToken ??= GrantId;
            subjectTokenType ??= DelegationConstants.DelegationGrantTokenType;
            var timestamp = ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            signature ??= DelegationSignature.Compute(
                DelegationConstants.BuildSignatureInput(tenantIdForSignature, subjectToken, nonce ?? string.Empty, timestamp),
                saltForSignature);

            return new TokenRequest
            {
                GrantType = GrantTypes.TokenExchange,
                TokenExchange = new TokenExchangeRequest
                {
                    SubjectToken = subjectToken,
                    SubjectTokenType = subjectTokenType,
                    Nonce = nonce,
                    Ts = timestamp.ToString(),
                    Signature = signature
                }
            };
        }

        private static Dictionary<string, List<string>> ClaimsOf(string accessToken)
        {
            var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            return token.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToList());
        }

        // ------------------------------------------------------------------ 11. happy path

        [Fact]
        public async Task ValidRequest_ShouldMintATokenCarryingTheRecordsUserOrgRolesAndPermissions()
        {
            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().BeNullOrWhiteSpace();
            result.TokenType.Should().Be("Bearer");
            result.AccessToken.Should().NotBeNullOrWhiteSpace();
            result.ExpiresIn.Should().BeGreaterThan(0);

            var claims = ClaimsOf(result.AccessToken!);
            claims["user_id"].Should().ContainSingle().Which.Should().Be(UserId);
            claims["tenant_id"].Should().ContainSingle().Which.Should().Be(TenantId);
            claims["org_id"].Should().ContainSingle().Which.Should().Be(OrganizationId);
            claims["roles"].Should().Contain("admin");
            claims["permissions"].Should().Contain("orders:write");
            claims["token_version"].Should().ContainSingle().Which.Should().Be("3");
            claims["security_stamp"].Should().ContainSingle().Which.Should().Be("stamp-3");
        }

        // ------------------------------------------------------------------ 21. no refresh token

        [Fact]
        public async Task ValidRequest_ShouldNotIssueARefreshToken()
        {
            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            // Delegation must never touch rotation: that is why a long job can retry forever.
            result.RefreshToken.Should().BeNull();
        }

        // ------------------------------------------------------------------ 22. MFA exemption

        [Fact]
        public void TokenExchange_ShouldBeExemptFromTheMfaCheckpoint()
        {
            // The grant was written while an already-authenticated user was in scope; a worker has
            // nobody to challenge.
            var exempt = typeof(OAuthJwtAccessTokenManager)
                .GetField("MfaCheckpointExemptGrantTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null) as HashSet<string>;

            exempt.Should().NotBeNull();
            exempt!.Should().Contain(GrantTypes.TokenExchange);
        }

        // ------------------------------------------------------------------ 12. signature

        [Fact]
        public async Task BadSignature_ShouldReturnInvalidClient_AndPerformNoRedisRead()
        {
            var result = await Create().AuthenticateAsync(
                Request(signature: new string('0', 64)), new IdentityConfiguration());

            result.Error.Should().Be("invalid_client");
            result.StatusCode.Should().Be(401);

            // The grant is never looked up until the signature passes.
            _cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SignatureFromAnotherTenantsSalt_ShouldBeRejected()
        {
            var result = await Create().AuthenticateAsync(
                Request(saltForSignature: "some-other-tenants-salt"), new IdentityConfiguration());

            result.Error.Should().Be("invalid_client");
            _cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SignatureOverADifferentTenantId_ShouldBeRejected()
        {
            // The tenant is bound into the signature input, so a signature made for another tenant
            // cannot be replayed here.
            var result = await Create().AuthenticateAsync(
                Request(tenantIdForSignature: "tenant-other"), new IdentityConfiguration());

            result.Error.Should().Be("invalid_client");
        }

        // ------------------------------------------------------------------ 13. clock window

        [Theory]
        [InlineData(-61)]
        [InlineData(61)]
        [InlineData(-3600)]
        public async Task TimestampOutsideTheClockWindow_ShouldReturnInvalidRequest(int offsetSeconds)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds;

            var result = await Create().AuthenticateAsync(Request(ts: ts), new IdentityConfiguration());

            result.Error.Should().Be("invalid_request");
            result.StatusCode.Should().Be(400);
        }

        [Theory]
        [InlineData(-59)]
        [InlineData(0)]
        [InlineData(59)]
        public async Task TimestampInsideTheClockWindow_ShouldBeAccepted(int offsetSeconds)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds;

            var result = await Create().AuthenticateAsync(Request(ts: ts), new IdentityConfiguration());

            result.Error.Should().BeNullOrWhiteSpace();
        }

        [Fact]
        public async Task NonNumericTimestamp_ShouldReturnInvalidRequest()
        {
            var request = Request();
            var exchange = request.TokenExchange!;
            request.TokenExchange = new TokenExchangeRequest
            {
                SubjectToken = exchange.SubjectToken,
                SubjectTokenType = exchange.SubjectTokenType,
                Nonce = exchange.Nonce,
                Ts = "not-a-number",
                Signature = exchange.Signature
            };

            var result = await Create().AuthenticateAsync(request, new IdentityConfiguration());

            result.Error.Should().Be("invalid_request");
        }

        // ------------------------------------------------------------------ 14. nonce replay

        [Fact]
        public async Task NonceReplay_ShouldReturnInvalidRequest()
        {
            RejectNonceAsReplay();

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_request");
            result.ErrorDescription.Should().Contain("replay");
            _cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task MissingNonce_ShouldReturnInvalidRequest()
        {
            var result = await Create().AuthenticateAsync(Request(nonce: ""), new IdentityConfiguration());

            result.Error.Should().Be("invalid_request");
        }

        [Fact]
        public async Task AnUnavailableNonceGuard_ShouldFailClosed()
        {
            _cacheDb
                .Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(), When.NotExists, It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            // Without a working replay guard the exchange must not proceed.
            result.Error.Should().Be("invalid_request");
        }

        // ------------------------------------------------------------------ 15. missing grant

        [Fact]
        public async Task UnknownOrExpiredGrant_ShouldReturnInvalidGrant()
        {
            StoreGrant(null);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task MalformedGrantId_ShouldBeRejectedBeforeAnyRedisAccess()
        {
            var result = await Create().AuthenticateAsync(
                Request(subjectToken: "dg_short"), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
            _cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
            _cacheDb.Verify(
                db => db.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()),
                Times.Never);
        }

        [Fact]
        public async Task WrongSubjectTokenType_ShouldReturnInvalidRequest()
        {
            var result = await Create().AuthenticateAsync(
                Request(subjectTokenType: "urn:ietf:params:oauth:token-type:access_token"),
                new IdentityConfiguration());

            result.Error.Should().Be("invalid_request");
        }

        [Fact]
        public async Task UnreadableStoredGrant_ShouldReturnInvalidGrant()
        {
            _cache
                .Setup(c => c.GetStringValueAsync(DelegationPolicy.GrantKey(GrantId)))
                .ReturnsAsync("{not-json");

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
        }

        // ------------------------------------------------------------------ 16. tenant mismatch

        [Fact]
        public async Task RecordTenantDifferentFromContextTenant_ShouldReturnInvalidGrant()
        {
            StoreGrant(Record(tenantId: "tenant-other"));

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
            _userRepository.Verify(r => r.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NoTenantInContext_ShouldReturnInvalidClient()
        {
            SetTenantContext(null);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_client");
            result.StatusCode.Should().Be(401);
        }

        [Fact]
        public async Task UnknownTenant_ShouldReturnInvalidClient()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns((Tenant?)null);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_client");
        }

        // ------------------------------------------------------------------ 17. record wins

        [Fact]
        public async Task TheRecordsUserWins_EvenWhenTheGrantIdIsPresentedForADifferentUser()
        {
            // A caller controls only the grant id and the signature. The user comes from the record,
            // so claiming another user's identity is impossible: this asserts the token carries the
            // record's user, not anything the caller supplied.
            StoreGrant(Record(userId: "user-from-record"));
            _userRepository
                .Setup(r => r.GetUserByIdAsync("user-from-record"))
                .ReturnsAsync(new User
                {
                    ItemId = "user-from-record",
                    Active = true,
                    TokenVersion = 3,
                    SecurityStamp = "stamp-3",
                    OrganizationIds = [OrganizationId],
                    Roles = new Dictionary<string, List<string>> { [OrganizationId] = ["viewer"] },
                    Permissions = new Dictionary<string, List<string>>()
                });

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            var claims = ClaimsOf(result.AccessToken!);
            claims["user_id"].Should().ContainSingle().Which.Should().Be("user-from-record");
            _userRepository.Verify(r => r.GetUserByIdAsync("user-from-record"), Times.Once);
        }

        [Fact]
        public async Task TheRecordsOrganizationWins()
        {
            StoreGrant(Record(organizationId: "org-from-record"));
            _userRepository
                .Setup(r => r.GetUserByIdAsync(UserId))
                .ReturnsAsync(new User
                {
                    ItemId = UserId,
                    Active = true,
                    TokenVersion = 3,
                    SecurityStamp = "stamp-3",
                    OrganizationIds = ["org-from-record", OrganizationId],
                    Roles = new Dictionary<string, List<string>>
                    {
                        ["org-from-record"] = ["record-role"],
                        [OrganizationId] = ["other-role"]
                    },
                    Permissions = new Dictionary<string, List<string>>()
                });

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            var claims = ClaimsOf(result.AccessToken!);
            claims["org_id"].Should().ContainSingle().Which.Should().Be("org-from-record");
            claims["roles"].Should().Contain("record-role").And.NotContain("other-role");
        }

        // ------------------------------------------------------------------ 18. version / stamp

        [Fact]
        public async Task ChangedTokenVersion_ShouldReturnInvalidGrant()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync(UserId)).ReturnsAsync(DelegatedUser(tokenVersion: 4));

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
            result.ErrorDescription.Should().Contain("Token version");
        }

        [Fact]
        public async Task ChangedSecurityStamp_ShouldReturnInvalidGrant()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync(UserId)).ReturnsAsync(DelegatedUser(securityStamp: "stamp-rotated"));

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
            result.ErrorDescription.Should().Contain("Security stamp");
        }

        [Fact]
        public async Task InactiveUser_ShouldReturnInvalidGrant()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync(UserId)).ReturnsAsync(DelegatedUser(active: false));

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
            result.ErrorDescription.Should().Contain("not active");
        }

        [Fact]
        public async Task DeletedUser_ShouldReturnInvalidGrant()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync(UserId)).ReturnsAsync((User?)null!);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("invalid_grant");
        }

        // ------------------------------------------------------------------ 19. permission drift

        [Fact]
        public async Task PermissionRemovedAfterTheGrantWasWritten_ShouldBeAbsentFromTheToken()
        {
            _userRepository
                .Setup(r => r.GetUserByIdAsync(UserId))
                .ReturnsAsync(new User
                {
                    ItemId = UserId,
                    Active = true,
                    TokenVersion = 3,
                    SecurityStamp = "stamp-3",
                    OrganizationIds = [OrganizationId],
                    Roles = new Dictionary<string, List<string>> { [OrganizationId] = ["admin"] },
                    // "orders:write" has been revoked since the grant was created.
                    Permissions = new Dictionary<string, List<string>> { [OrganizationId] = ["orders:read"] }
                });

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            var claims = ClaimsOf(result.AccessToken!);
            claims["permissions"].Should().Contain("orders:read").And.NotContain("orders:write");
        }

        [Fact]
        public async Task OrganizationMembershipRemovedAfterTheGrantWasWritten_ShouldYieldNoAuthority()
        {
            // Roles and permissions are per-organization dictionaries, and AuthorizationClaimsResolver
            // does NOT fall back to "default" for a named organization it cannot find. So a user
            // removed from the grant's organization gets a token with no roles and no permissions --
            // it grants nothing, even though the user still has authority elsewhere.
            _userRepository
                .Setup(r => r.GetUserByIdAsync(UserId))
                .ReturnsAsync(new User
                {
                    ItemId = UserId,
                    Active = true,
                    TokenVersion = 3,
                    SecurityStamp = "stamp-3",
                    OrganizationIds = ["default"],
                    Roles = new Dictionary<string, List<string>> { ["default"] = ["owner"] },
                    Permissions = new Dictionary<string, List<string>> { ["default"] = ["everything"] }
                });

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            var claims = ClaimsOf(result.AccessToken!);
            claims.Should().NotContainKey("roles");
            claims.Should().NotContainKey("permissions");

            // Specifically: the authority attached to "default" must not leak into a token minted
            // for org-1.
            result.AccessToken!.Should().NotContain("owner");
            result.AccessToken!.Should().NotContain("everything");
        }

        // ------------------------------------------------------------------ 20. rate cap

        [Fact]
        public async Task RateCapExceeded_ShouldReturn429SlowDown()
        {
            _cacheDb
                .Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(DelegationPolicy.RedemptionsPerWindow + 1);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            result.Error.Should().Be("slow_down");
            result.StatusCode.Should().Be(429);
        }

        [Fact]
        public async Task TheRateWindowIsSetOnlyOnTheFirstRedemption()
        {
            _cacheDb
                .Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(2L);

            await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            _cacheDb.Verify(
                db => db.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()),
                Times.Never);
        }

        [Fact]
        public async Task AnUnavailableRateCounter_ShouldNotBlockTheExchange()
        {
            _cacheDb
                .Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            // A broken counter is a availability concern, not an authorization one: every other
            // check still stands.
            result.Error.Should().BeNullOrWhiteSpace();
        }

        // ------------------------------------------------------------------ misc contract

        [Fact]
        public async Task MissingExchangeParameters_ShouldReturnInvalidRequest()
        {
            var result = await Create().AuthenticateAsync(
                new TokenRequest { GrantType = GrantTypes.TokenExchange }, new IdentityConfiguration());

            result.Error.Should().Be("invalid_request");
        }

        [Fact]
        public async Task MissingAuthenticationConfiguration_ShouldReturnServerError()
        {
            var result = await Create().AuthenticateAsync(Request(), null!);

            result.Error.Should().Be("server_error");
            result.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task TheGrantIsNeverDeletedByTheExchange()
        {
            // The worker settles and deletes. A redemption must leave the grant in place so the same
            // job can exchange again, and so a retry after a crash still works.
            await Create().AuthenticateAsync(Request(), new IdentityConfiguration());

            _cache.Verify(c => c.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
            _cacheDb.Verify(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task TheSameGrantCanBeRedeemedRepeatedly()
        {
            var service = Create();

            for (var i = 0; i < 5; i++)
            {
                var result = await service.AuthenticateAsync(Request(nonce: $"nonce-{i:x32}"), new IdentityConfiguration());
                result.Error.Should().BeNullOrWhiteSpace();
                result.RefreshToken.Should().BeNull();
            }
        }
    }
}
