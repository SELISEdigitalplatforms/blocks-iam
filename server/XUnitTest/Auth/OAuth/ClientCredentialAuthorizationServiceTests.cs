using System.Security.Claims;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class ClientCredentialAuthorizationServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<ICertificateProviderFactory> _certFactory = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();

        private ClientCredentialAuthorizationService Create() =>
            new(_authRepo.Object, _certFactory.Object, _crypto.Object, _cache.Object, _tenants.Object);

        private static IdentityConfiguration Config() => new();

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
