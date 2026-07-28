using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// Additional coverage for the OIDC services in <c>OidcServices.cs</c>, focused on the tenant
    /// certificate resolution paths and the discovery cache-hit branch that the baseline suite does not
    /// reach. A generated self-signed certificate drives the real certificate-loading code.
    /// </summary>
    public sealed class OidcServicesCoverageTests : IDisposable
    {
        private const string CertPassword = "pwd";

        private readonly OidcSigningKeyMaterial _keyMaterial = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IDatabase> _cacheDb = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly Mock<ICertificateProviderFactory> _certFactory = new();
        private readonly Mock<ICertificateProvider> _certProvider = new();
        private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IConfiguration> _configuration = new();

        public OidcServicesCoverageTests()
        {
            _cache.Setup(c => c.CacheDatabase()).Returns(_cacheDb.Object);
            _crypto.Setup(c => c.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("cache-key");
            _certFactory.Setup(f => f.GetProvider(It.IsAny<CertificateStorageType>())).Returns(_certProvider.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static byte[] GenerateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=blocks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return cert.Export(X509ContentType.Pkcs12, CertPassword);
        }

        private static Tenant MakeTenant() => new()
        {
            TenantId = "tenant-1",
            ItemId = "tenant-item-1",
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters
            {
                Issuer = "https://issuer",
                PrivateCertificatePassword = CertPassword,
                PublicCertificatePassword = CertPassword,
                IssueDate = DateTime.UtcNow
            }
        };

        private void SetTenantContext(string tenantId = "tenant-1")
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: tenantId, impersonationSessionId: null, applicationDomain: "test"));
        }

        private TokenGenerationService TokenGen() => new(
            _keyMaterial, _tenants.Object, _cache.Object, null!, _crypto.Object,
            _certFactory.Object, _httpContextAccessor.Object, _authRepo.Object);

        [Fact]
        public async Task GenerateAccessTokenAsync_WithTenantCertificate_SignsWithTenantIssuer()
        {
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(GenerateCertificate());
            var claims = new OidcClaims { Sub = "blocks|u1", TenantId = "tenant-1", ClientId = "c1", Audience = "aud" };

            var jwt = await TokenGen().GenerateAccessTokenAsync(claims, "https://iss", 3600);

            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt);
            token.Issuer.Should().Be("https://issuer");
        }

        [Fact]
        public async Task GenerateAccessTokenAsync_TenantCertCacheMiss_FetchesFromProvider()
        {
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(RedisValue.Null);
            _certProvider.Setup(p => p.GetCertificateAsync(It.IsAny<string>())).ReturnsAsync(GenerateCertificate());
            var claims = new OidcClaims { Sub = "blocks|u1", TenantId = "tenant-1", ClientId = "c1" };

            var jwt = await TokenGen().GenerateAccessTokenAsync(claims, "https://iss", 3600);

            jwt.Should().NotBeNullOrWhiteSpace();
            _certProvider.Verify(p => p.GetCertificateAsync(It.IsAny<string>()), Times.Once);
        }

        private DiscoveryService Discovery() =>
            new(_httpContextAccessor.Object, _configuration.Object, _tenants.Object, _cache.Object);

        [Fact]
        public async Task DiscoveryService_GetMetadataAsync_CacheHit_Deserializes()
        {
            var cached = new DiscoveryMetadata { Issuer = "https://cached", TokenEndpoint = "https://cached/token" };
            _cacheDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(JsonSerializer.Serialize(cached));

            var metadata = await Discovery().GetMetadataAsync();

            metadata.Issuer.Should().Be("https://cached");
        }

        [Fact]
        public async Task DiscoveryService_GetAuthorizationServerMetadataAsync_CacheHit_Deserializes()
        {
            var cached = new OAuthAuthorizationServerMetadata { Issuer = "https://cached-oauth", TokenEndpoint = "https://cached-oauth/token" };
            _cacheDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(JsonSerializer.Serialize(cached));

            var metadata = await Discovery().GetAuthorizationServerMetadataAsync();

            metadata.Issuer.Should().Be("https://cached-oauth");
        }

        private JwksService Jwks() => new(
            _keyMaterial, _tenants.Object, _cache.Object, _httpContextAccessor.Object, _crypto.Object, _certFactory.Object);

        [Fact]
        public async Task JwksService_WithTenantPrivateCertificate_ReturnsTenantKey()
        {
            SetTenantContext();
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            _cacheDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(GenerateCertificate());

            var response = await Jwks().GetKeysAsync();

            response.Keys.Should().ContainSingle();
            response.Keys[0].N.Should().NotBeNullOrWhiteSpace();
            response.Keys[0].E.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task JwksService_TenantPrivateCertCacheMiss_FetchesFromProvider()
        {
            SetTenantContext();
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(MakeTenant());
            _cacheDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(RedisValue.Null);
            _certProvider.Setup(p => p.GetCertificateAsync(It.IsAny<string>())).ReturnsAsync(GenerateCertificate());

            var response = await Jwks().GetKeysAsync();

            response.Keys.Should().ContainSingle();
            _certProvider.Verify(p => p.GetCertificateAsync(It.IsAny<string>()), Times.Once);
        }
    }
}
