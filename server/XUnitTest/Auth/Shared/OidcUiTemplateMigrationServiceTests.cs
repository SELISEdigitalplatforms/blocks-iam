using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Migrations;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Auth.Shared
{
    public sealed class OidcUiTemplateMigrationServiceTests : IDisposable
    {
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IAuthenticationRepository> _repository = new();
        private readonly Mock<ILegacyOidcClientBrandingReader> _reader = new();
        private readonly Mock<ILogger<OidcUiTemplateMigrationService>> _logger = new();
        private readonly Dictionary<string, OidcUiTemplate> _templates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<LegacyOidcClientBranding>> _clients = new(StringComparer.Ordinal);

        public OidcUiTemplateMigrationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            _repository.Setup(r => r.GetOidcUiTemplateAsync())
                .ReturnsAsync(() => _templates.GetValueOrDefault(CurrentTenantId()));
            _repository.Setup(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()))
                .Callback<OidcUiTemplate>(template => _templates[CurrentTenantId()] = template)
                .Returns(Task.CompletedTask);
            _reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string databaseName, string _, CancellationToken _) =>
                    _clients.GetValueOrDefault(databaseName) ?? []);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private OidcUiTemplateMigrationService Create() =>
            new(_tenants.Object, _repository.Object, _reader.Object, _logger.Object);

        [Fact]
        public async Task SingleClient_MigratesLogoAndValidColorOntoCompleteDefaults()
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] =
            [
                Client("client-1", active: true, logo: "https://t1.example/logo.png", color: "#123456")
            ];

            var summary = await Create().RunAsync();

            summary.TenantsMigrated.Should().Be(1);
            var migrated = _templates["tenant-1"];
            migrated.Branding!.LogoUrl.Should().Be("https://t1.example/logo.png");
            migrated.Theme!.Primary.Should().Be("#123456");
            migrated.Theme.Secondary.Should().Be(IdpService.CreateDefaultOidcUiTemplate().Theme!.Secondary);
            migrated.Pages.Should().BeEquivalentTo(IdpService.CreateDefaultOidcUiTemplate().Pages);
            Guid.TryParse(migrated.ItemId, out _).Should().BeTrue();
        }

        [Fact]
        public async Task MultipleClients_PrefersActiveThenUsesOrdinalClientIdTieBreak()
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] =
            [
                Client("active-z", active: true, color: "#999999"),
                Client("inactive-a", active: false, logo: "https://inactive.example/logo.png", color: "#111111"),
                Client("active-a", active: true, logo: "https://winner.example/logo.png", color: "#222222")
            ];

            await Create().RunAsync();

            _templates["tenant-1"].Branding!.LogoUrl.Should().Be("https://winner.example/logo.png");
            _templates["tenant-1"].Theme!.Primary.Should().Be("#222222");
        }

        [Fact]
        public async Task ActiveSource_DoesNotBlendMissingLogoFromInactiveClient()
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] =
            [
                Client("client-a", active: false, logo: "https://inactive.example/logo.png"),
                Client("client-b", active: true, color: "#123456")
            ];

            await Create().RunAsync();

            _templates["tenant-1"].Theme!.Primary.Should().Be("#123456");
            _templates["tenant-1"].Branding!.LogoUrl.Should().BeNull();
        }

        [Fact]
        public async Task NoActiveBrandedClient_FallsBackToLowestOrdinalClientId()
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] =
            [
                Client("z-client", active: false, color: "#999999"),
                Client("a-client", active: false, color: "#111111"),
                Client("active-without-branding", active: true)
            ];

            await Create().RunAsync();

            _templates["tenant-1"].Theme!.Primary.Should().Be("#111111");
        }

        [Theory]
        [InlineData("cornflowerblue")]
        [InlineData("#abcd")]
        [InlineData("rgba(1,2,3,1)")]
        public async Task InvalidLegacyColor_LeavesDefaultButStillMigratesLogo(string legacyColor)
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] =
            [
                Client("client-1", active: true, logo: "https://t1.example/logo.png", color: legacyColor)
            ];

            await Create().RunAsync();

            var migrated = _templates["tenant-1"];
            migrated.Branding!.LogoUrl.Should().Be("https://t1.example/logo.png");
            migrated.Theme!.Primary.Should().Be("#0066b2");
        }

        [Fact]
        public async Task TenantWithoutLegacyBranding_IsSkippedWithoutCreatingTemplate()
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] = [Client("client-1", active: true)];

            var summary = await Create().RunAsync();

            summary.TenantsSkipped.Should().Be(1);
            summary.TenantsMigrated.Should().Be(0);
            _templates.Should().BeEmpty();
            _repository.Verify(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()), Times.Never);
        }

        [Fact]
        public async Task ExistingTemplate_IsNeverReadFromLegacySourceOrOverwritten()
        {
            ConfigureTenants("tenant-1");
            var existing = IdpService.CreateDefaultOidcUiTemplate();
            existing.Branding!.BrandName = "Admin customization";
            _templates["tenant-1"] = existing;
            _clients["db-tenant-1"] = [Client("client-1", active: true, color: "#123456")];

            var summary = await Create().RunAsync();

            summary.TenantsSkipped.Should().Be(1);
            _templates["tenant-1"].Should().BeSameAs(existing);
            _reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _repository.Verify(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()), Times.Never);
        }

        [Fact]
        public async Task RunningTwice_IsIdempotentAndSavesExactlyOnce()
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] = [Client("client-1", active: true, color: "#123456")];
            var service = Create();

            var first = await service.RunAsync();
            var saved = _templates["tenant-1"];
            var second = await service.RunAsync();

            first.TenantsMigrated.Should().Be(1);
            second.TenantsMigrated.Should().Be(0);
            second.TenantsSkipped.Should().Be(1);
            _templates["tenant-1"].Should().BeSameAs(saved);
            _repository.Verify(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()), Times.Once);
        }

        [Fact]
        public async Task PerTenantWriteFailure_IsLoggedAndDoesNotAbortRemainingTenants()
        {
            ConfigureTenants("tenant-bad", "tenant-good");
            _clients["db-tenant-bad"] = [Client("bad", active: true, color: "#111111")];
            _clients["db-tenant-good"] = [Client("good", active: true, color: "#222222")];
            _repository.Setup(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()))
                .Returns<OidcUiTemplate>(template =>
                {
                    if (CurrentTenantId() == "tenant-bad")
                    {
                        throw new InvalidOperationException("write failed");
                    }

                    _templates[CurrentTenantId()] = template;
                    return Task.CompletedTask;
                });

            var summary = await Create().RunAsync();

            summary.TenantsFailed.Should().Be(1);
            summary.TenantsMigrated.Should().Be(1);
            _templates.Should().ContainKey("tenant-good").WhoseValue.Theme!.Primary.Should().Be("#222222");
            _templates.Should().NotContainKey("tenant-bad");
            _logger.Verify(logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        [Fact]
        public async Task Migration_UsesEachTenantTupleAndRestoresOriginalAmbientContext()
        {
            ConfigureTenants("tenant-1");
            _clients["db-tenant-1"] = [Client("client-1", active: true, color: "#abc")];
            var original = BlocksContext.Create(
                tenantId: "original", roles: null, userId: "user", impersonated: false,
                isAuthenticated: true, requestUri: string.Empty, organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: string.Empty,
                userName: "user", phoneNumber: null, displayName: "User", oauthToken: null,
                originalTenantId: "original", impersonationSessionId: null, applicationDomain: string.Empty);
            BlocksContext.SetContext(original);

            await Create().RunAsync();

            BlocksContext.GetContext().Should().BeSameAs(original);
            _reader.Verify(r => r.ReadAsync("db-tenant-1", "connection-tenant-1", It.IsAny<CancellationToken>()), Times.Once);
        }

        private void ConfigureTenants(params string[] tenantIds)
        {
            var values = tenantIds.ToDictionary(
                tenantId => tenantId,
                tenantId => ($"db-{tenantId}", $"connection-{tenantId}"),
                StringComparer.Ordinal);
            _tenants.Setup(t => t.GetTenantDatabaseConnectionStrings()).Returns(values);
        }

        private static LegacyOidcClientBranding Client(
            string clientId,
            bool active,
            string? logo = null,
            string? color = null) => new()
        {
            ClientId = clientId,
            IsActive = active,
            LegacyLogoUrl = logo,
            LegacyBrandColor = color
        };

        private static string CurrentTenantId() =>
            BlocksContext.GetContext()?.TenantId ?? throw new InvalidOperationException("Missing migration context.");
    }
}
