using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class DeviceCodeExchangeServiceTests
    {
        private const string DeviceCode = "raw-device-code-1";
        private readonly DeviceCodeGenerator _generator = new();

        private static (DeviceCodeExchangeService svc, Mock<IDeviceAuthorizationRepository> repo, Mock<IUserRepository> userRepo, Mock<IAuthenticationRepository> authRepo) Build()
        {
            var repo = new Mock<IDeviceAuthorizationRepository>();
            var userRepo = new Mock<IUserRepository>();
            var authRepo = new Mock<IAuthenticationRepository>();
            var mint = new Mock<IOidcTokenMintService>();
            mint.Setup(m => m.MintAsync(It.IsAny<OidcTokenMintRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OidcTokenMintResult { AccessToken = "at", IdToken = "it", RefreshToken = "rt", Scope = "openid", ExpiresIn = 600, AccessExpiry = DateTime.UtcNow, RefreshExpiry = DateTime.UtcNow.AddDays(1) });

            var svc = new DeviceCodeExchangeService(
                repo.Object,
                new DeviceCodeGenerator(),
                mint.Object,
                authRepo.Object,
                userRepo.Object,
                Options.Create(new DeviceFlowOptions()),
                NullLogger<DeviceCodeExchangeService>.Instance);

            return (svc, repo, userRepo, authRepo);
        }

        private static HttpRequest BuildRequest(string? deviceCode, string? clientId, string? clientSecret = null)
        {
            var ctx = new DefaultHttpContext();
            var form = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();
            if (deviceCode != null) form["device_code"] = deviceCode;
            if (clientId != null) form["client_id"] = clientId;
            if (clientSecret != null) form["client_secret"] = clientSecret;
            ctx.Request.Form = new FormCollection(form);
            return ctx.Request;
        }

        private static OidcClientRegistration DeviceClient(string clientId = "c1") => new()
        {
            ClientId = clientId,
            IsActive = true,
            IsDeviceFlowClient = true,
            ClientType = "public",
            TokenEndpointAuthMethod = "none",
            AllowedScopes = new List<string> { "openid", "offline_access" }
        };

        private static OidcClientRegistration ConfidentialDeviceClient(string clientId = "c1") => new()
        {
            ClientId = clientId,
            ClientSecret = "secret-1",
            IsActive = true,
            IsDeviceFlowClient = true,
            ClientType = "confidential",
            TokenEndpointAuthMethod = "client_secret_post",
            AllowedScopes = new List<string> { "openid" }
        };

        [Fact]
        public async Task ExchangeAsync_ReturnsInvalidGrant_WhenDeviceCodeMissing()
        {
            var (svc, _, _, _) = Build();
            var result = await svc.ExchangeAsync(BuildRequest(null, "c1"));
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("invalid_grant");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsInvalidGrant_WhenUnknownDeviceCode()
        {
            var (svc, repo, _, authRepo) = Build();
            repo.Setup(r => r.GetByDeviceCodeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeviceAuthorizationRequestModel?)null);

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("invalid_grant");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsAccessDenied_WhenDenied()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Denied
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("access_denied");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsExpiredToken_WhenExpired()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Expired
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("expired_token");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsAuthorizationPending_WhenPending()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Pending,
                LastPollAt = DateTime.UtcNow.AddSeconds(-10),
                PollIntervalSeconds = 5,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());
            repo.Setup(r => r.TryRecordPollAsync("id1", entity.LastPollAt, It.IsAny<DateTime>(), 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("authorization_pending");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsSlowDown_WhenPollingTooFast()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Pending,
                LastPollAt = DateTime.UtcNow.AddSeconds(-1),
                PollIntervalSeconds = 5,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());
            repo.Setup(r => r.BumpPollIntervalAsync("id1", 5, 5, It.IsAny<CancellationToken>())).ReturnsAsync(10);

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("slow_down");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsSlowDown_WhenPollCasFails()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Pending,
                LastPollAt = DateTime.UtcNow.AddSeconds(-10),
                PollIntervalSeconds = 5,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());
            repo.Setup(r => r.TryRecordPollAsync("id1", entity.LastPollAt, It.IsAny<DateTime>(), 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            repo.Setup(r => r.BumpPollIntervalAsync("id1", 5, 5, It.IsAny<CancellationToken>())).ReturnsAsync(10);

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("slow_down");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsInvalidClient_WhenConfidentialClientSecretMissing()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Pending,
                LastPollAt = DateTime.UtcNow.AddSeconds(-10),
                PollIntervalSeconds = 5,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(ConfidentialDeviceClient());

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task ExchangeAsync_AllowsConfidentialClient_WhenSecretMatches()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Pending,
                LastPollAt = DateTime.UtcNow.AddSeconds(-10),
                PollIntervalSeconds = 5,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(ConfidentialDeviceClient());
            repo.Setup(r => r.TryRecordPollAsync("id1", entity.LastPollAt, It.IsAny<DateTime>(), 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1", "secret-1"));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("authorization_pending");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsInvalidGrant_WhenConsumed()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Consumed
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("invalid_grant");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsAccessDenied_WhenCasFails()
        {
            var (svc, repo, _, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Approved,
                UserId = "user-1",
                RequestedScopes = "openid",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            repo.Setup(r => r.MarkConsumedAsync("id1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value!.ToString().Should().Contain("access_denied");
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsTokenJson_OnApproved()
        {
            var (svc, repo, userRepo, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id1", DeviceCodeHash = hash, UserCode = "ABCD-EFGH", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Approved,
                UserId = "user-1",
                RequestedScopes = "openid offline_access",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            repo.Setup(r => r.MarkConsumedAsync("id1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            userRepo.Setup(u => u.GetUserByIdAsync("user-1")).ReturnsAsync(new Iam.DomainService.Entities.User { ItemId = "user-1", Email = "x@y.z" });
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task ExchangeAsync_ReturnsTokenJson_WithoutRefreshToken_WhenOfflineAccessMissing()
        {
            var (svc, repo, userRepo, authRepo) = Build();
            var hash = _generator.HashDeviceCode(DeviceCode);
            var entity = new DeviceAuthorizationRequestModel
            {
                Id = "id2", DeviceCodeHash = hash, UserCode = "WXYZ-2345", ClientId = "c1", TenantId = "t1",
                Status = DeviceAuthorizationStatus.Approved,
                UserId = "user-2",
                RequestedScopes = "openid",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            repo.Setup(r => r.GetByDeviceCodeHashAsync(hash, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            repo.Setup(r => r.MarkConsumedAsync("id2", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            userRepo.Setup(u => u.GetUserByIdAsync("user-2")).ReturnsAsync(new Iam.DomainService.Entities.User { ItemId = "user-2" });
            authRepo.Setup(a => a.GetOidcClientRegistrationAsync("c1")).ReturnsAsync(DeviceClient());

            var result = await svc.ExchangeAsync(BuildRequest(DeviceCode, "c1"));
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var dict = ok.Value.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
            dict.Keys.Should().Contain("access_token");
            dict.Keys.Should().NotContain("refresh_token");
        }
    }
}
