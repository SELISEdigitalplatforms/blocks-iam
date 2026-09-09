using Authentication.DomainService.Authentication;
using Authentication.DomainService.Authentication.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Moq;

namespace XUnitTest.Auth
{
    public class AuthenticationConfigurationServiceTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ITenants> _tenants = new();

        public AuthenticationConfigurationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "user-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "org-1",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private const string IamBaseUrl = "https://dev-iam.blocksdevelopers.com";

        private AuthenticationConfigurationService Create(string? iamBaseUrl = IamBaseUrl)
            => new(_repo.Object, _tenants.Object, BuildConfiguration(iamBaseUrl));

        private static IConfiguration BuildConfiguration(string? iamBaseUrl)
        {
            var values = new Dictionary<string, string?>();

            if (iamBaseUrl != null)
            {
                values["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = iamBaseUrl;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static Tenant TenantWithApps(params string[] domains)
        {
            return new Tenant
            {
                TenantId = "tenant-1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = string.Empty,
                    PublicCertificatePath = "certs/pub.pem",
                    IssueDate = DateTime.UtcNow
                },
                Applications = domains.Select(d => new Applications { Domain = d }).ToList()
            };
        }

        [Fact]
        public async Task GetAuthenticationConfig_ReturnsOk_WithCertPath()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { ItemId = ObjectId.GenerateNewId() });
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));

            var result = await Create().GetAuthenticationConfigAsync();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Update_Fails_WhenNoBaseUrl_AndOidcDisabled_AndUseDefault()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = null!
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("AccountActionBaseUrl");
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()), Times.Never);
        }

        [Fact]
        public async Task Update_Fails_WhenBaseUrl_NotInTenantAllowedDomains()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://evil.attacker.com"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("AccountActionBaseUrl");
        }

        [Fact]
        public async Task Update_Succeeds_WhenBaseUrl_InTenantAllowedDomains()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://app.example.com"
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()), Times.Once);
        }

        [Fact]
        public async Task Update_Succeeds_WhenOidcEnabled_AndNoBaseUrl()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true
            });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Update_ResolvesValuesFromCurrent_WhenRequestFieldsUnset()
        {
            var current = new IdentityConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                AccessTokenValidForNumberMinutes = 42,
                AccountActionBaseUrl = "https://app.example.com",
                IsOidcEnabled = true
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011"
                // AccessTokenValidForNumberMinutes left as 0 -> should fall back to current (42)
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.AccessTokenValidForNumberMinutes.Should().Be(42);
        }

        [Fact]
        public async Task Update_PersistsCollectPasswordOnActivation_WhenRequestSetsIt()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                CollectPasswordOnActivation = false
            });

            result.IsSuccess.Should().BeTrue();
            saved.Should().NotBeNull();
            saved!.CollectPasswordOnActivation.Should().BeFalse();
        }

        [Fact]
        public async Task Update_KeepsStoredCollectPasswordOnActivation_WhenRequestOmitsIt()
        {
            var current = new IdentityConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                AccountActionBaseUrl = "https://app.example.com",
                IsOidcEnabled = true,
                CollectPasswordOnActivation = false
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011"
            });

            result.IsSuccess.Should().BeTrue();
            saved!.CollectPasswordOnActivation.Should().BeFalse();
        }

        [Fact]
        public async Task Update_DefaultsCollectPasswordOnActivationToTrue_WhenNeitherRequestNorCurrentHasIt()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true
            });

            result.IsSuccess.Should().BeTrue();
            saved!.CollectPasswordOnActivation.Should().BeTrue();
        }
        [Fact]
        public async Task Update_ForcesConfiguredIamBaseUrl_AndIgnoresPayload_WhenOidcEnabled()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://whatever.the-caller-asked-for.com"
            });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be(IamBaseUrl);
        }

        [Fact]
        public async Task Update_AcceptsIamBaseUrlOutsideTenantDomains_WhenOidcEnabled()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            // The IAM host is deliberately not one of the tenant's application domains.
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = true,
                UseAccountActionBaseUrlAsDefault = false
            });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be(IamBaseUrl);
        }

        [Fact]
        public async Task Update_KeepsStoredBaseUrl_WhenOidcEnabled_AndIamBaseUrlUnresolvable()
        {
            var current = new IdentityConfiguration
            {
                ItemId = ObjectId.GenerateNewId(),
                AccountActionBaseUrl = "https://already-stored.example.com",
                IsOidcEnabled = true
            };
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(current);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create(iamBaseUrl: null).UpdateAuthenticationConfigAsync(
                new UpdateAuthenticationConfigurationRequest
                {
                    ItemId = "507f1f77bcf86cd799439011",
                    IsOidcEnabled = true,
                    AccountActionBaseUrl = "https://whatever.the-caller-asked-for.com"
                });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be("https://already-stored.example.com");
        }

        [Fact]
        public async Task Update_StillHonoursPayloadBaseUrl_WhenOidcDisabled()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(TenantWithApps("https://app.example.com"));
            IdentityConfiguration? saved = null;
            _repo.Setup(r => r.UpdateAuthenticationConfigurationAsync(It.IsAny<IdentityConfiguration>()))
                .Callback<IdentityConfiguration>(c => saved = c)
                .Returns(Task.CompletedTask);

            var result = await Create().UpdateAuthenticationConfigAsync(new UpdateAuthenticationConfigurationRequest
            {
                ItemId = "507f1f77bcf86cd799439011",
                IsOidcEnabled = false,
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://app.example.com"
            });

            result.IsSuccess.Should().BeTrue();
            saved!.AccountActionBaseUrl.Should().Be("https://app.example.com");
        }
    }
}
