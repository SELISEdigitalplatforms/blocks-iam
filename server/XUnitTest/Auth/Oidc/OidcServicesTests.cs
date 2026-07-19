using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace XUnitTest.Auth.Oidc
{
    public class OidcServicesTests : IDisposable
    {
        public OidcServicesTests()
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

        private static Mock<ICacheClient> CacheWithEmptyDb()
        {
            var cache = new Mock<ICacheClient>();
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);
            db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
            cache.Setup(c => c.CacheDatabase()).Returns(db.Object);
            return cache;
        }

        // ---------- PkceService ----------

        [Fact]
        public async Task Pkce_WrongMethod_ReturnsFalse()
        {
            var svc = new PkceService();
            (await svc.ValidateVerifierAsync("challenge", "verifier", "plain")).Should().BeFalse();
        }

        [Fact]
        public async Task Pkce_ValidS256_ReturnsTrue()
        {
            var svc = new PkceService();
            var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
            using var sha = SHA256.Create();
            var challenge = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

            (await svc.ValidateVerifierAsync(challenge, verifier, "S256")).Should().BeTrue();
        }

        [Fact]
        public async Task Pkce_MismatchedVerifier_ReturnsFalse()
        {
            var svc = new PkceService();
            (await svc.ValidateVerifierAsync("some-challenge", "wrong-verifier", "S256")).Should().BeFalse();
        }

        [Fact]
        public void Pkce_GenerateRandomCode_Returns43Chars()
        {
            var code = new PkceService().GenerateRandomCode(32);
            code.Length.Should().Be(43);
            code.Should().NotContain("/").And.NotContain("+");
        }

        // ---------- TokenGenerationService (null-tenant path uses in-memory key) ----------

        private static TokenGenerationService CreateTokenSvc()
        {
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            return new TokenGenerationService(
                new OidcSigningKeyMaterial(),
                tenants.Object,
                new Mock<ICacheClient>().Object,
                null!,
                new Mock<ICryptoService>().Object,
                new Mock<ICertificateProviderFactory>().Object,
                new Mock<IHttpContextAccessor>().Object,
                new Mock<IAuthenticationRepository>().Object);
        }

        [Fact]
        public async Task GenerateIdToken_ContainsSubjectNonceAndTokenUse()
        {
            var claims = new OidcClaims
            {
                Sub = "blocks|user-1", TenantId = "tenant-1", ClientId = "cid",
                Nonce = "n-123", Email = "u@x.com", Name = "User One", UserName = "u1",
                Iat = 1000, AuthTime = 900
            };
            var jwt = await CreateTokenSvc().GenerateIdTokenAsync(claims, "https://issuer", 3600);

            var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
            token.Claims.Should().Contain(c => c.Type == "nonce" && c.Value == "n-123");
            token.Claims.Should().Contain(c => c.Type == "token_use" && c.Value == "id_token");
            token.Claims.Should().Contain(c => c.Type == BlocksContext.USER_ID_CLAIM && c.Value == "user-1");
        }

        [Fact]
        public async Task GenerateAccessToken_ContainsRolesPermissionsAndTokenUse()
        {
            var claims = new OidcClaims
            {
                Sub = "user-1", TenantId = "tenant-1", ClientId = "cid", Audience = "aud",
                Roles = new List<string> { "admin" }, Permissions = new List<string> { "read" },
                Iat = 1000, AuthTime = 900
            };
            var jwt = await CreateTokenSvc().GenerateAccessTokenAsync(claims, "https://issuer", 3600);

            var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
            token.Claims.Should().Contain(c => c.Type == "token_use" && c.Value == "access_token");
            token.Claims.Should().Contain(c => c.Type == BlocksContext.ROLES_CLAIM && c.Value == "admin");
            token.Claims.Should().Contain(c => c.Type == BlocksContext.PERMISSION_CLAIM && c.Value == "read");
            token.Claims.Should().NotContain(c => c.Type == "nonce");
        }

        // ---------- JwksService (fallback key path) ----------

        [Fact]
        public async Task Jwks_NoTenant_ReturnsFallbackKey()
        {
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            var svc = new JwksService(
                new OidcSigningKeyMaterial(),
                tenants.Object,
                new Mock<ICacheClient>().Object,
                new Mock<IHttpContextAccessor>().Object,
                new Mock<ICryptoService>().Object,
                new Mock<ICertificateProviderFactory>().Object);

            var result = await svc.GetKeysAsync();

            result.Keys.Should().HaveCount(1);
            result.Keys[0].Kid.Should().NotBeNullOrWhiteSpace();
            result.Keys[0].N.Should().NotBeNullOrWhiteSpace();
            result.Keys[0].E.Should().NotBeNullOrWhiteSpace();
        }

        // ---------- DiscoveryService ----------

        private static DiscoveryService CreateDiscovery()
        {
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            var config = new Mock<IConfiguration>();
            config.Setup(c => c[It.IsAny<string>()]).Returns((string)null!);
            return new DiscoveryService(new Mock<IHttpContextAccessor>().Object, config.Object, tenants.Object, CacheWithEmptyDb().Object);
        }

        [Fact]
        public async Task Discovery_GetMetadata_BuildsEndpoints()
        {
            var metadata = await CreateDiscovery().GetMetadataAsync();
            metadata.Should().NotBeNull();
            metadata.AuthorizationEndpoint.Should().Contain("oidc/authorize");
            metadata.TokenEndpoint.Should().Contain("oidc/token");
            metadata.JwksUri.Should().Contain("jwks.json");
        }

        [Fact]
        public async Task Discovery_GetAuthorizationServerMetadata_BuildsEndpoints()
        {
            var metadata = await CreateDiscovery().GetAuthorizationServerMetadataAsync();
            metadata.Should().NotBeNull();
            metadata.TokenEndpoint.Should().Contain("oidc/token");
            metadata.RevocationEndpoint.Should().Contain("oidc/revoke");
            metadata.IntrospectionEndpoint.Should().Contain("oidc/introspect");
        }
    }
}
