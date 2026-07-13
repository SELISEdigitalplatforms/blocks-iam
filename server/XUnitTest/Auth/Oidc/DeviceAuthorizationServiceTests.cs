using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class DeviceAuthorizationServiceTests
    {
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

        private static IFormCollection EmptyForm() => new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());

        [Fact]
        public async Task RequestAsync_Throws_WhenClientIdMissing()
        {
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), new Mock<IAuthenticationRepository>());
            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "", TenantId = "tenant-1" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_request");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenTenantIdMissing()
        {
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), new Mock<IAuthenticationRepository>());
            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "cli", TenantId = "" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_request");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenTenantNotFound()
        {
            var tenants = new Mock<Blocks.Genesis.ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            var authRepo = new Mock<IAuthenticationRepository>();
            var service = new DeviceAuthorizationService(
                new Mock<IDeviceAuthorizationRepository>().Object,
                new DeviceCodeGenerator(),
                authRepo.Object,
                tenants.Object,
                NullLogger<DeviceAuthorizationService>.Instance);

            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "cli", TenantId = "missing" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_tenant");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenClientNotFound()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            authRepo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync((OidcClientRegistration?)null);

            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), authRepo);
            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "missing", TenantId = "t1" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_client");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenClientIsNotDeviceFlowEnabled()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            var client = new OidcClientRegistration { ClientId = "c1", IsDeviceFlowClient = false, IsActive = true, AllowedScopes = new List<string> { "openid" } };
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), authRepo, client);

            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "c1", TenantId = "t1", Scope = "openid" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "unauthorized_client");
        }

        [Fact]
        public async Task RequestAsync_Throws_WhenScopeNotAllowed()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            var client = new OidcClientRegistration { ClientId = "c1", IsDeviceFlowClient = true, IsActive = true, AllowedScopes = new List<string> { "openid" } };
            var service = CreateService(new Mock<IDeviceAuthorizationRepository>(), authRepo, client);

            var act = async () => await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "c1", TenantId = "t1", Scope = "forbidden other" }, new DefaultHttpContext().Request);
            await act.Should().ThrowAsync<DeviceAuthorizationException>().Where(e => e.Error == "invalid_scope");
        }

        [Fact]
        public async Task RequestAsync_ReturnsStandardRfc8628Payload_OnSuccess()
        {
            var client = new OidcClientRegistration { ClientId = "c1", IsDeviceFlowClient = true, IsActive = true, AllowedScopes = new List<string> { "openid", "profile" } };
            var repo = new Mock<IDeviceAuthorizationRepository>();
            repo.Setup(r => r.CreateAsync(It.IsAny<DeviceAuthorizationRequestModel>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            repo.Setup(r => r.GetByUserCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DeviceAuthorizationRequestModel?)null);

            var authRepo = new Mock<IAuthenticationRepository>();
            var service = CreateService(repo, authRepo, client);

            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("idp.example.com");

            var response = await service.RequestAsync(new DeviceAuthorizationRequest { ClientId = "c1", TenantId = "t1", Scope = "openid profile" }, ctx.Request);

            response.Should().NotBeNull();
            response.DeviceCode.Should().NotBeNullOrEmpty();
            response.UserCode.Should().NotBeNullOrEmpty();
            response.UserCode.Should().HaveLength(9).And.Contain("-");
            response.VerificationUri.Should().StartWith("https://idp.example.com/device");
            response.VerificationUriComplete.Should().Contain("user_code=");
            response.ExpiresIn.Should().Be(600);
            response.Interval.Should().Be(5);

            repo.Verify(r => r.CreateAsync(It.Is<DeviceAuthorizationRequestModel>(m =>
                m.ClientId == "c1"
                && m.TenantId == "t1"
                && m.Status == DeviceAuthorizationStatus.Pending
                && !string.IsNullOrEmpty(m.DeviceCodeHash)
                && !string.IsNullOrEmpty(m.UserCode)
            ), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}