using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 §7 example 6 / C1: a non-impersonated token-exchange grant's organization is explicit, so a
    /// disabled one is refused with 403 and nothing is minted; an active one is redeemed as before.
    /// </summary>
    public sealed class TokenExchangeOrganizationGateTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string TenantSalt = "tenant-salt-value";
        private const string UserId = "user-1";
        private const string A = "org-a";
        private const string D = "org-d";
        private static readonly string GrantId = "dg_" + new string('a', 64);

        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IDatabase> _cacheDb = new();
        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly Mock<IJwtAccessTokenProvider> _provider = new();
        private readonly Mock<IResourceRepository> _resources = new();
        private readonly bool _originalTestMode = BlocksContext.IsTestMode;

        public TokenExchangeOrganizationGateTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: null, isAuthenticated: false,
                requestUri: null, organizationId: null, expireOn: DateTime.MinValue,
                email: null, permissions: null, userName: null, phoneNumber: null,
                displayName: null, oauthToken: null, originalTenantId: TenantId));

            var tenant = DisabledOrganizationsKit.SigningTenant();
            tenant.TenantSalt = TenantSalt;
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            _cache.Setup(c => c.CacheDatabase()).Returns(_cacheDb.Object);
            _cacheDb.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), When.NotExists, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            _cacheDb.Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>())).ReturnsAsync(1L);
            _cacheDb.Setup(db => db.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
            _userRepository.Setup(r => r.GetUserByIdAsync(UserId)).ReturnsAsync(new User
            {
                ItemId = UserId, Active = true, TokenVersion = 3, SecurityStamp = "stamp-3", OrganizationIds = [A, D]
            });
            _resources.MultiOrg();
            _resources.WithOrganizationStatuses(disabled: [D]);

            // The real decision and the real mint behind a mock, so calls can be counted.
            var real = DisabledOrganizationsKit.RealProvider(_resources);
            _provider.Setup(p => p.ResolveOrganizationAsync(It.IsAny<User>(), It.IsAny<TokenRequest>()))
                .Returns((User user, TokenRequest request) => real.ResolveOrganizationAsync(user, request));
            _provider.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo?>()))
                .Returns((IdentityConfiguration c, Tenant t, User u, TokenRequest r, StateInfo? s) => real.GetJwtAccessToken(c, t, u, r, s));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = _originalTestMode;
        }

        private TokenExchangeAuthorizationService Create() => new(
            _tenants.Object, _cache.Object, _userRepository.Object, _provider.Object,
            new Mock<Authentication.DomainService.Services.IAuthenticationRepository>().Object,
            new Mock<IClientAccessTokenIssuer>().Object, NullLogger<TokenExchangeAuthorizationService>.Instance);

        private void StoreGrant(string organizationId) =>
            _cache.Setup(c => c.GetStringValueAsync(DelegationPolicy.GrantKey(GrantId)))
                .ReturnsAsync(JsonSerializer.Serialize(new DelegationGrantRecord
                {
                    TenantId = TenantId, UserId = UserId, OrganizationId = organizationId, TokenVersion = "3", SecurityStamp = "stamp-3"
                }));

        private static TokenRequest Request()
        {
            const string nonce = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new TokenRequest
            {
                GrantType = GrantTypes.TokenExchange,
                TokenExchange = new TokenExchangeRequest
                {
                    SubjectToken = GrantId,
                    SubjectTokenType = DelegationConstants.DelegationGrantTokenType,
                    Nonce = nonce,
                    Ts = timestamp.ToString(),
                    Signature = DelegationSignature.Compute(DelegationConstants.BuildSignatureInput(TenantId, GrantId, nonce, timestamp), TenantSalt)
                }
            };
        }

        [Fact]
        public async Task C1_Example6_TokenExchangeGrantOrganizationDisabled_403_NothingMinted()
        {
            StoreGrant(D);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration { AccessTokenValidForNumberMinutes = 5 });

            result.StatusCode.Should().Be(403);
            result.Error.Should().Be("organization_disabled");
            result.AccessToken.Should().BeNullOrEmpty();
            _provider.Verify(p => p.ResolveOrganizationAsync(It.IsAny<User>(),
                It.Is<TokenRequest>(r => r.OrganizationSource == OrganizationRequestSource.Explicit && r.OrganizationId == D)), Times.Once);
            _provider.Verify(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo?>()), Times.Never);
        }

        [Fact]
        public async Task Example6_TokenExchangeGrantOrganizationActive_RedeemedAsBefore()
        {
            StoreGrant(A);

            var result = await Create().AuthenticateAsync(Request(), new IdentityConfiguration { AccessTokenValidForNumberMinutes = 5 });

            result.Error.Should().BeNullOrWhiteSpace();
            result.AccessToken.Should().NotBeNullOrWhiteSpace();
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken)
                .Claims.Should().ContainSingle(c => c.Type == BlocksContext.ORGANIZATION_ID_CLAIM).Which.Value.Should().Be(A);
            _resources.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
        }
    }
}
