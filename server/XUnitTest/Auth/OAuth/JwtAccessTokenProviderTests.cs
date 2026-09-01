using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Iam.DomainService.Resources;
using Iam.DomainService.Utilities;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// Unit tests for <see cref="JwtAccessTokenProvider"/>. A self-signed PKCS#12 certificate is generated
    /// in-process so the signing-credential and token-mapping paths run for real; the static claim builder,
    /// the certificate cache retrieval branches and the null-certificate short-circuit are all covered.
    /// </summary>
    public sealed class JwtAccessTokenProviderTests
    {
        private const string CertPassword = "pwd";

        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IDatabase> _cacheDb = new();
        private readonly Mock<ICryptoService> _cryptoService = new();
        private readonly Mock<ICertificateProviderFactory> _certificateProviderFactory = new();
        private readonly Mock<ICertificateProvider> _certificateProvider = new();
        private readonly Mock<IAuthorizationClaimsResolver> _claimsResolver = new();
        private readonly Mock<IResourceRepository> _resourceRepository = new();

        public JwtAccessTokenProviderTests()
        {
            _cacheClient.Setup(c => c.CacheDatabase()).Returns(_cacheDb.Object);
            _cryptoService.Setup(c => c.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("cache-key");
            _certificateProviderFactory.Setup(f => f.GetProvider(It.IsAny<CertificateStorageType>())).Returns(_certificateProvider.Object);
        }

        private JwtAccessTokenProvider Sut() => new(
            NullLogger<JwtAccessTokenProvider>.Instance, _cacheClient.Object, _cryptoService.Object,
            _certificateProviderFactory.Object, _claimsResolver.Object, _resourceRepository.Object);

        private static OrganizationScope Scope(string organizationId) =>
            new(OrganizationScopeKind.Organization, organizationId);

        private static OrganizationScope TenantWideScope() =>
            new(OrganizationScopeKind.TenantWide, IdpConstants.DefaultOrganizationId);

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
                IssueDate = DateTime.UtcNow,
                CertificateStorageType = CertificateStorageType.Azure
            }
        };

        private static User MakeUser() => new()
        {
            ItemId = "u1",
            SecurityStamp = "stamp",
            TokenVersion = 2,
            OrganizationIds = new List<string> { "org1" }
        };

        private static IdentityConfiguration Config() => new()
        {
            AccessTokenValidForNumberMinutes = 10,
            RefreshTokenValidForNumberMinutes = 60
        };

        [Fact]
        public void AddClaims_NonImpersonation_AddsTenantAndResolvedOrg()
        {
            var identity = new ClaimsIdentity();
            var claims = new ResolvedAuthorizationClaims { Roles = { "admin" }, Permissions = { "read" } };
            var request = new TokenRequest { OrganizationId = "org1", IsImpersonation = false };

            JwtAccessTokenProvider.AddClaims(identity, MakeTenant(), MakeUser(), claims, request, Scope("org1"));

            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("tenant-1");
            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("org1");
            identity.FindAll(BlocksContext.ROLES_CLAIM).Should().ContainSingle(c => c.Value == "admin");
            identity.FindAll(BlocksContext.PERMISSION_CLAIM).Should().ContainSingle(c => c.Value == "read");
        }

        [Fact] // The unknown-organization fallback now lives in OrganizationScopeResolver
        // (OrganizationScopeResolverTests.UnauthorisedRequestedOrganization_IsDiscarded_NotEchoed);
        // AddClaims only mirrors whatever scope it is handed.
        public void AddClaims_EmitsTheScopeItIsGiven_IgnoringTheRequestedOrganization()
        {
            var identity = new ClaimsIdentity();
            var request = new TokenRequest { OrganizationId = "not-a-member", IsImpersonation = false };

            JwtAccessTokenProvider.AddClaims(identity, MakeTenant(), MakeUser(), new ResolvedAuthorizationClaims(), request, TenantWideScope());

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("default");
        }

        [Fact]
        public void AddClaims_Impersonation_AddsImpersonationClaimsAndNonce()
        {
            var identity = new ClaimsIdentity();
            var request = new TokenRequest
            {
                IsImpersonation = true,
                OriginalTenantId = "orig",
                TargetTenantId = "target",
                ImpersonationSessionId = "sess1"
            };
            var state = new StateInfo { ClientId = "c", Provider = "p", Audience = "a", Nonce = "nonce-1" };

            JwtAccessTokenProvider.AddClaims(identity, MakeTenant(), MakeUser(), new ResolvedAuthorizationClaims(), request, TenantWideScope(), state);

            identity.FindFirst(BlocksContext.IMPERSONATED_CLAIM)!.Value.Should().Be("true");
            identity.FindFirst(BlocksContext.ORIGINAL_TENANT_ID_CLAIM)!.Value.Should().Be("orig");
            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("target");
            identity.FindFirst(BlocksContext.IMPERSONATION_SESSION_ID_CLAIM)!.Value.Should().Be("sess1");
            identity.FindFirst("nonce")!.Value.Should().Be("nonce-1");
        }

        [Fact]
        public void MakeSigningCredentials_ValidCertificate_ReturnsRsaCredentials()
        {
            var credentials = JwtAccessTokenProvider.MakeSigningCredentials(GenerateCertificate(), CertPassword);
            credentials.Algorithm.Should().Be(Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256);
            credentials.Key.Should().NotBeNull();
        }

        [Fact]
        public void MakeSigningCredentials_InvalidData_Throws()
        {
            Action act = () => JwtAccessTokenProvider.MakeSigningCredentials(new byte[] { 1, 2, 3 }, "wrong");
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void MapJwtAccessToken_BuildsTokenWithClaimsAndExpiry()
        {
            var token = Sut().MapJwtAccessToken(
                Config(), MakeTenant(), MakeUser(), GenerateCertificate(),
                new ResolvedAuthorizationClaims { Roles = { "admin" } },
                new TokenRequest { OrganizationId = "org1" }, Scope("org1"));

            token.Issuer.Should().Be("https://issuer");
            token.AccessTokenValidForNumberMinute.Should().Be(10);
            token.Claims.Should().Contain(c => c.Type == BlocksContext.USER_ID_CLAIM && c.Value == "u1");
            token.SigningCredentials.Should().NotBeNull();
        }

        [Fact]
        public async Task GetOrRetrieveCertAsync_CacheMiss_FetchesFromProviderAndCaches()
        {
            var pfx = GenerateCertificate();
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(RedisValue.Null);
            _certificateProvider.Setup(p => p.GetCertificateAsync(It.IsAny<string>())).ReturnsAsync(pfx);

            var result = await Sut().GetOrRetrieveCertAsync(MakeTenant());

            result.Should().NotBeNull();
            result!.Length.Should().Be(pfx.Length);
            _certificateProvider.Verify(p => p.GetCertificateAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetOrRetrieveCertAsync_CacheHit_ReturnsCachedWithoutProvider()
        {
            var pfx = GenerateCertificate();
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(pfx);

            var result = await Sut().GetOrRetrieveCertAsync(MakeTenant());

            result.Should().NotBeNull();
            _certificateProvider.Verify(p => p.GetCertificateAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetJwtAccessToken_NullCertificate_ReturnsEmptyToken()
        {
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(RedisValue.Null);
            _certificateProvider.Setup(p => p.GetCertificateAsync(It.IsAny<string>())).ReturnsAsync((byte[])null!);

            var token = await Sut().GetJwtAccessToken(Config(), MakeTenant(), MakeUser(), new TokenRequest());

            token.Issuer.Should().BeNull();
            token.SigningCredentials.Should().BeNull();
            token.Claims.Should().BeEmpty();
        }

        [Fact]
        public async Task GetJwtAccessToken_WithCertificate_ResolvesClaimsAndMapsToken()
        {
            var pfx = GenerateCertificate();
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(pfx);
            _claimsResolver.Setup(r => r.ResolveAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
                .ReturnsAsync(new ResolvedAuthorizationClaims { Roles = { "admin" } });

            var token = await Sut().GetJwtAccessToken(Config(), MakeTenant(), MakeUser(), new TokenRequest { OrganizationId = "org1" });

            token.Issuer.Should().Be("https://issuer");
            token.Claims.Should().Contain(c => c.Type == BlocksContext.ROLES_CLAIM && c.Value == "admin");
        }
    }
}
