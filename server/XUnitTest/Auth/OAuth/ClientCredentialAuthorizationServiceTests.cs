using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.OAuth
{
    public class ClientCredentialAuthorizationServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<ICertificateProviderFactory> _certFactory = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        private ClientCredentialAuthorizationService Create() =>
            new(_authRepo.Object, _certFactory.Object, _crypto.Object, _cache.Object, _tenants.Object,
                TestDoubles.ClientCredentialSecretTestDoubles.Resolver(_secrets));

        private static IdentityConfiguration Config() => new();

        private const string CertPassword = "pwd";
        private readonly Mock<IDatabase> _cacheDb = new();
        private readonly Mock<ICertificateProvider> _certProvider = new();

        private void WireCertInfrastructure()
        {
            _cache.Setup(c => c.CacheDatabase()).Returns(_cacheDb.Object);
            _crypto.Setup(c => c.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("cache-key");
            _certFactory.Setup(f => f.GetProvider(It.IsAny<CertificateStorageType>())).Returns(_certProvider.Object);
        }

        private static byte[] GenerateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=blocks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return cert.Export(X509ContentType.Pkcs12, CertPassword);
        }

        private static Tenant CertTenant() => new()
        {
            TenantId = "tenant-1",
            ItemId = "tenant-item-1",
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters
            {
                Issuer = "https://issuer",
                PrivateCertificatePassword = CertPassword,
                IssueDate = DateTime.UtcNow
            }
        };

        private static ClientCredential ActiveClient() =>
            new() { ItemId = "c1", OrganizationId = "default", ClientSecret = "secret", IsActive = true, AccessTokenValidForNumberMinutes = 15 };

        [Fact]
        public async Task Authenticate_TenantMissing_ReturnsServerError()
        {
            WireCertInfrastructure();
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync(ActiveClient());
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var result = await Create().AuthenticateAsync(
                new TokenRequest { ClientId = "c1", ClientSecret = "secret" }, Config());

            result.Error.Should().Be("server_error");
            result.ErrorDescription.Should().Contain("tenant");
        }

        [Fact]
        public async Task Authenticate_Success_ReturnsBearerTokenWithClientLifetime()
        {
            WireCertInfrastructure();
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync(ActiveClient());
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(CertTenant());
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(GenerateCertificate());

            var result = await Create().AuthenticateAsync(
                new TokenRequest { ClientId = "c1", ClientSecret = "secret" }, Config());

            result.Error.Should().BeNull();
            result.TokenType.Should().Be("Bearer");
            result.StatusCode.Should().Be(200);
            result.ExpiresIn.Should().Be(15 * 60);
            result.AccessToken.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task RetrievePrivateCertAsync_CacheMiss_FetchesFromProvider()
        {
            WireCertInfrastructure();
            var pfx = GenerateCertificate();
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(RedisValue.Null);
            _certProvider.Setup(p => p.GetCertificateAsync(It.IsAny<string>())).ReturnsAsync(pfx);

            var result = await Create().RetrievePrivateCertAsync(CertTenant());

            result!.Length.Should().Be(pfx.Length);
            _certProvider.Verify(p => p.GetCertificateAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RetrievePrivateCertAsync_CacheHit_SkipsProvider()
        {
            WireCertInfrastructure();
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(GenerateCertificate());

            var result = await Create().RetrievePrivateCertAsync(CertTenant());

            result.Should().NotBeNull();
            _certProvider.Verify(p => p.GetCertificateAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Authenticate_NullConfiguration_ReturnsServerError()
        {
            var result = await Create().AuthenticateAsync(new TokenRequest { ClientId = "c1" }, null!);
            result.Error.Should().Be("server_error");
        }

        [Fact]
        public async Task Authenticate_ClientNotFound_ReturnsInvalidClient()
        {
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync((ClientCredential)null!);

            var result = await Create().AuthenticateAsync(
                new TokenRequest { ClientId = "c1", ClientSecret = "s" }, Config());

            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("No client found");
        }

        [Fact]
        public async Task Authenticate_WrongSecret_ReturnsInvalidClient()
        {
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1"))
                .ReturnsAsync(new ClientCredential { ClientSecret = "correct", IsActive = true });

            var result = await Create().AuthenticateAsync(
                new TokenRequest { ClientId = "c1", ClientSecret = "wrong" }, Config());

            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("Client secret not match");
        }

        [Fact]
        public async Task Authenticate_MissingSecret_ReturnsInvalidClient()
        {
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1"))
                .ReturnsAsync(new ClientCredential { ClientSecret = "correct", IsActive = true });

            var result = await Create().AuthenticateAsync(
                new TokenRequest { ClientId = "c1", ClientSecret = null }, Config());

            result.Error.Should().Be("invalid_client");
        }

        // ---------- secret in Blocks Secrets ----------

        private static ClientCredential ReferencedClient(string staleField = "") =>
            new() { ItemId = "c1", OrganizationId = "default", ClientSecret = staleField, ClientSecretReference = "cs-ref", IsActive = true, AccessTokenValidForNumberMinutes = 15 };

        [Fact]
        public async Task Authenticate_ReferencedSecret_IsComparedWithTheStoreValue_AndIssuesTheToken()
        {
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            WireCertInfrastructure();
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync(ReferencedClient());
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ReturnsAsync("blxk_store");
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(CertTenant());
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(GenerateCertificate());

            var result = await Create().AuthenticateAsync(new TokenRequest { ClientId = "c1", ClientSecret = "blxk_store" }, Config());

            result.Error.Should().BeNull();
            result.AccessToken.Should().NotBeNullOrWhiteSpace();
            _secrets.Verify(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Authenticate_ReferencedSecret_WrongSecret_ReturnsInvalidClient()
        {
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync(ReferencedClient());
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ReturnsAsync("blxk_store");

            var result = await Create().AuthenticateAsync(new TokenRequest { ClientId = "c1", ClientSecret = "blxk_other" }, Config());

            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("Client secret not match");
        }

        [Fact]
        public async Task Authenticate_ReferencedSecret_DoesNotAcceptAPlaintextLeftInTheDocument()
        {
            // The reference is authoritative: a value still in ClientSecret is never compared.
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync(ReferencedClient(staleField: "blxk_stale"));
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ReturnsAsync("blxk_store");

            var result = await Create().AuthenticateAsync(new TokenRequest { ClientId = "c1", ClientSecret = "blxk_stale" }, Config());

            result.Error.Should().Be("invalid_client");
        }

        [Fact]
        public async Task Authenticate_ReferencedSecret_StoreUnavailable_Returns503_NotInvalidClient()
        {
            using var context = TestDoubles.IdentityProviderSecretTestDoubles.AnonymousTenant();
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync(ReferencedClient(staleField: "blxk_stale"));
            _secrets.Setup(s => s.GetValueAsync("cs-ref", It.IsAny<CancellationToken>())).ThrowsAsync(new SecretVaultException("down cs-ref", "get", "cs-ref"));

            var result = await Create().AuthenticateAsync(new TokenRequest { ClientId = "c1", ClientSecret = "blxk_stale" }, Config());

            result.Error.Should().Be("temporarily_unavailable");
            result.StatusCode.Should().Be(503);
            result.AccessToken.Should().BeNull();
            result.ErrorDescription.Should().NotContain("cs-ref");
        }

        [Fact]
        public async Task Authenticate_LegacyPlaintext_MakesNoSecretCall()
        {
            WireCertInfrastructure();
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1")).ReturnsAsync(ActiveClient());
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(CertTenant());
            _cacheDb.Setup(d => d.StringGet(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Returns(GenerateCertificate());

            var result = await Create().AuthenticateAsync(new TokenRequest { ClientId = "c1", ClientSecret = "secret" }, Config());

            result.Error.Should().BeNull();
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Authenticate_InactiveClient_ReturnsInvalidClient()
        {
            _authRepo.Setup(r => r.GetClientCredentialByIdAsync("c1"))
                .ReturnsAsync(new ClientCredential { ClientSecret = "secret", IsActive = false });

            var result = await Create().AuthenticateAsync(
                new TokenRequest { ClientId = "c1", ClientSecret = "secret" }, Config());

            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("Client is not active");
        }

        [Fact]
        public void AddClaims_PopulatesTenantSubjectRolesAndPermissions()
        {
            var identity = new ClaimsIdentity("test");
            var tenant = new Tenant
            {
                TenantId = "tenant-9",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
                Applications = new List<Applications>()
            };
            var client = new ClientCredential
            {
                ItemId = "client-item-1",
                OrganizationId = "org-7",
                Roles = new List<string> { "reader", "writer" },
                Permissions = new List<string> { "res.read" }
            };

            ClientCredentialAuthorizationService.AddClaims(identity, tenant, client);

            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("tenant-9");
            identity.FindFirst(BlocksContext.SUBJECT_CLAIM)!.Value.Should().Be("blocks|client-item-1");
            identity.FindFirst("client_id")!.Value.Should().Be("client-item-1");
            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("org-7");
            identity.FindAll(BlocksContext.ROLES_CLAIM).Select(c => c.Value).Should().BeEquivalentTo("reader", "writer");
            identity.FindAll(BlocksContext.PERMISSION_CLAIM).Select(c => c.Value).Should().BeEquivalentTo("res.read");
        }
    }
}
