using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class DeviceAuthorizationServiceTests : IDisposable
    {
        private static void SetContext(string tenantId)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId,
                roles: null,
                userId: "user-1",
                impersonated: false,
                isAuthenticated: true,
                requestUri: "https://test/device",
                organizationId: "org-1",
                permissions: null,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "user@example.com",
                userName: "tester",
                phoneNumber: null,
                displayName: "Tester",
                oauthToken: null,
                originalTenantId: tenantId,
                impersonationSessionId: null,
                applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static DeviceAuthorizationService CreateService(
            Mock<IDeviceAuthorizationRepository> repo,
            Mock<IAuthenticationRepository> authRepo,
            OidcClientRegistration? client = null,
            Tenant? tenant = null)
        {
            var tenants = new Mock<Blocks.Genesis.ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(tenant ?? BuildTenant("tenant-1"));

            authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync(client);

            return new DeviceAuthorizationService(
                repo.Object,
                new DeviceCodeGenerator(),
                authRepo.Object,
                tenants.Object,
                Options.Create(new DeviceFlowOptions()),
                NullLogger<DeviceAuthorizationService>.Instance);
        }

        private static Tenant BuildTenant(string id)
        {
            return new Tenant
            {
                TenantId = id,
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = string.Empty,
                    IssueDate = DateTime.UtcNow
                },
                Applications = new List<Applications>()
            };
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenClientIdMissing()
        {
            SetContext("tenant-1");
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), new Mock<IAuthenticationRepository>());
            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_request");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenTenantIdMissing()
        {
            SetContext("");
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), new Mock<IAuthenticationRepository>());
            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "cli" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_request");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenTenantNotFound()
        {
            SetContext("missing");
            var tenants = new Mock<Blocks.Genesis.ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            var authRepo = new Mock<IAuthenticationRepository>();
            var service = new DeviceAuthorizationService(
                new Mock<IDeviceAuthorizationRepository>().Object,
                new DeviceCodeGenerator(),
                authRepo.Object,
                tenants.Object,
                Options.Create(new DeviceFlowOptions()),
                NullLogger<DeviceAuthorizationService>.Instance);

            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "cli" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_tenant");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenClientNotFound()
        {
            SetContext("t1");
            var authRepo = new Mock<IAuthenticationRepository>();
            authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration?)null);

            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), authRepo);
            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "missing" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_client");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenClientIsNotDeviceFlowEnabled()
        {
            SetContext("t1");
            var authRepo = new Mock<IAuthenticationRepository>();
            var client = new OidcClientRegistration { ClientId = "c1", IsDeviceFlowClient = false, IsActive = true, AllowedScopes = new List<string> { "openid" } };
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), authRepo, client);

            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "c1", Scope = "openid" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "unauthorized_client");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenScopeNotAllowed()
        {
            SetContext("t1");
            var authRepo = new Mock<IAuthenticationRepository>();
            var client = new OidcClientRegistration { ClientId = "c1", IsDeviceFlowClient = true, IsActive = true, AllowedScopes = new List<string> { "openid" } };
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), authRepo, client);

            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "c1", Scope = "forbidden other" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_scope");
        }

        [Fact]
        public async Task RequestAsync_ReturnsStandardRfc8628Payload_OnSuccess()
        {
            SetContext("t1");
            var client = new OidcClientRegistration { ClientId = "c1", IsDeviceFlowClient = true, IsActive = true, AllowedScopes = new List<string> { "openid", "profile", "offline_access" } };
            var repo = new Mock<IDeviceAuthorizationRepository>();
            repo.Setup(r => r.CreateAsync(It.IsAny<DeviceAuthorizationRequestModel>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DeviceAuthorizationRequestModel?)null);

            var authRepo = new Mock<IAuthenticationRepository>();
            var service = CreateService(repo, authRepo, client);

            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("idp.example.com");

            var response = await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "c1", Scope = "openid profile" }, ctx.Request);

            response.Should().NotBeNull();
            response.DeviceCode.Should().NotBeNullOrEmpty();
            response.UserCode.Should().NotBeNullOrEmpty();
            response.UserCode.Should().HaveLength(9).And.Contain("-");
            response.VerificationUri.Should().StartWith("https://idp.example.com/device/t1");
            response.VerificationUriComplete.Should().Be("https://idp.example.com/device/t1?user_code=" + response.UserCode);
            response.ExpiresIn.Should().Be(600);
            response.Interval.Should().Be(5);

            repo.Verify(r => r.CreateAsync(It.Is<DeviceAuthorizationRequestModel>(m =>
                m.ClientId == "c1"
                && m.TenantId == "t1"
                && m.Status == DeviceAuthorizationStatus.Pending
                && m.RequestedScopes == "openid profile offline_access"
                && !string.IsNullOrEmpty(m.DeviceCodeHash)
                && !string.IsNullOrEmpty(m.UserCode)
            ), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
