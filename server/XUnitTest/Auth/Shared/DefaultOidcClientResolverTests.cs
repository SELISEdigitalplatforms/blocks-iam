using Authentication.DomainService.Services;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// The default client feeds emailed links, which open in a browser: only web redirect URIs
    /// count, and a mobile app-scheme URI is never handed out.
    /// </summary>
    public class DefaultOidcClientResolverTests
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();

        private DefaultOidcClientResolver Create() =>
            new(_repo.Object, NullLogger<DefaultOidcClientResolver>.Instance);

        private static OidcClientRegistration Client(string clientId, bool isActive, params string[] redirectUris) => new()
        {
            ClientId = clientId,
            IsActive = isActive,
            RedirectUris = redirectUris.ToList()
        };

        private void Clients(params OidcClientRegistration[] clients) =>
            _repo.Setup(r => r.GetOIDCCredentialsByTenantAsync()).ReturnsAsync(clients.ToList());

        [Fact]
        public async Task GetDefaultClient_SkipsClientWithOnlyAppSchemeUris()
        {
            Clients(
                Client("A", true, "com.a.app://cb"),
                Client("B", true, "https://b.example.com/cb"));

            var result = await Create().GetDefaultClientAsync();

            result.Should().Be(new Iam.DomainService.Services.DefaultOidcClient("B", "https://b.example.com/cb"));
        }

        [Fact]
        public async Task GetDefaultClient_UsesFirstWebUri_WhenAppSchemeUriComesFirst()
        {
            Clients(Client("C", true, "com.c.app://cb", "https://c.example.com/cb"));

            var result = await Create().GetDefaultClientAsync();

            result.Should().Be(new Iam.DomainService.Services.DefaultOidcClient("C", "https://c.example.com/cb"));
        }

        [Fact]
        public async Task GetDefaultClient_ReturnsNull_WhenOnlyAppSchemeUrisExist()
        {
            Clients(
                Client("A", true, "com.a.app://cb"),
                Client("B", true, "com.b.app:/oauth2redirect"));

            var result = await Create().GetDefaultClientAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetDefaultClient_ReturnsNull_WhenNoActiveClientHasAWebUri()
        {
            Clients(
                Client("inactive", false, "https://inactive.example.com/cb"),
                Client("", true, "https://no-id.example.com/cb"),
                Client("blank", true, " ", "not a uri"),
                Client("app", true, "com.app.example://cb"));

            var result = await Create().GetDefaultClientAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetDefaultClient_KeepsRepositoryOrder_AmongWebClients()
        {
            Clients(
                Client("first", true, "http://first.example.com/cb"),
                Client("second", true, "https://second.example.com/cb"));

            var result = await Create().GetDefaultClientAsync();

            result!.ClientId.Should().Be("first");
            result.RedirectUri.Should().Be("http://first.example.com/cb");
        }

        [Fact]
        public async Task GetDefaultClient_ReturnsNull_WhenRepositoryThrows()
        {
            _repo.Setup(r => r.GetOIDCCredentialsByTenantAsync()).ThrowsAsync(new InvalidOperationException("db down"));

            var result = await Create().GetDefaultClientAsync();

            result.Should().BeNull();
        }
    }
}
