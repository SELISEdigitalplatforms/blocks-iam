using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using System.Text.Json;
using RtaTests = XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests;

namespace XUnitTest.Auth.Oidc
{
    public class OidcRefreshTokenServiceTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepo = new();

        // Inner (real) RefreshTokenAuthenticationService dependencies.
        private readonly Mock<IJwtAccessTokenProvider> _innerJwt = new();
        private readonly Mock<ITenants> _innerTenants = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _innerTokenMgr = new();
        private readonly Mock<IAuthenticationRepository> _innerAuthRepo = new();

        public OidcRefreshTokenServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private RefreshTokenAuthenticationService BuildInner() =>
            new(NullLogger<RefreshTokenAuthenticationService>.Instance, _innerJwt.Object, _innerTenants.Object, _innerTokenMgr.Object, _innerAuthRepo.Object);

        private OidcRefreshTokenService Create() =>
            new(_authRepo.Object, _cache.Object, _tenants.Object, BuildInner(), _authService.Object, _refreshTokenRepo.Object, NullLogger<OidcRefreshTokenService>.Instance);

        private static HttpRequest MakeRequest(Dictionary<string, string>? form = null)
        {
            var ctx = new DefaultHttpContext();
            var dict = (form ?? new Dictionary<string, string>())
                .ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value));
            ctx.Request.Form = new FormCollection(dict);
            return ctx.Request;
        }

        private static string SerializeCache(RefreshTokenCache cache) => JsonSerializer.Serialize(cache);

        // Wires everything needed for ValidateRefreshTokenAsync to pass.
        private void SetupValidRefreshToken(RefreshTokenCache tokenCache, User user)
        {
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(SerializeCache(tokenCache));
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(tokenCache.ClientId!))
                .ReturnsAsync(new OidcClientRegistration { ClientId = tokenCache.ClientId!, UseTokensCookie = false });
            _authRepo.Setup(r => r.GetUserByIdAsync(tokenCache.UserId!)).ReturnsAsync(user);
        }

        private void SetupSuccessfulInner()
        {
            _innerTenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(RtaTests.MakeTenant());
            _innerJwt.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(RtaTests.MakeJwtAccessToken());
            _innerTokenMgr.Setup(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()))
                .ReturnsAsync(("new-refresh-token", DateTime.UtcNow.AddMinutes(30)));
        }

        // ---------- early request validation ----------

        [Fact]
        public async Task Rotate_MissingClientId_ReturnsBadRequest()
        {
            var result = await Create().RotateAsync(MakeRequest());
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_request", error_description = "Missing client_id" });
        }

        [Fact]
        public async Task Rotate_ClientNotFound_ReturnsBadRequestInvalidClient()
        {
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("c1")).ReturnsAsync((OidcClientRegistration)null!);

            var result = await Create().RotateAsync(MakeRequest(new() { ["client_id"] = "c1" }));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_client", error_description = "client not found" });
        }

        [Fact]
        public async Task Rotate_NoRefreshToken_ReturnsBadRequest()
        {
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "c1", UseTokensCookie = false });

            var result = await Create().RotateAsync(MakeRequest(new() { ["client_id"] = "c1" }));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_request", error_description = "refresh token not found" });
        }

        // ---------- ValidateRefreshTokenAsync branches ----------

        private Dictionary<string, string> FormWithToken(string clientId = "c1", string token = "rt-1") =>
            new() { ["client_id"] = clientId, ["refresh_token"] = token };

        private void SetupClient(string clientId = "c1") =>
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync(clientId))
                .ReturnsAsync(new OidcClientRegistration { ClientId = clientId, UseTokensCookie = false });

        [Fact]
        public async Task Rotate_ConfigurationMissing_ReturnsAuthConfigMissing()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = OAuthError.AuthConfigMissing });
        }

        [Fact]
        public async Task Rotate_CachedTokenMissing_ReturnsInvalidGrant()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_grant", error_description = "Refresh token is invalid or expired" });
        }

        [Fact]
        public async Task Rotate_TokenCacheEmptyUserId_ReturnsInvalidGrant()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync(SerializeCache(new RefreshTokenCache { UserId = "", ClientId = "c1", TenantId = "tenant-1" }));

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Rotate_ClientRegistrationForTokenMissing_ReturnsUnauthorizedInvalidClient()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            // The token's client id resolves to no registration.
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync(SerializeCache(new RefreshTokenCache { UserId = "u1", ClientId = "missing-client", TenantId = "tenant-1" }));
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("missing-client")).ReturnsAsync((OidcClientRegistration)null!);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauth.Value.Should().BeEquivalentTo(new { error = "invalid_client", error_description = "Client configuration not found" });
        }

        [Fact]
        public async Task Rotate_TenantMismatch_ReturnsInvalidGrant()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync(SerializeCache(new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "other-tenant" }));
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "c1", UseTokensCookie = false });

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_grant", error_description = "Refresh token tenant mismatch" });
        }

        [Fact]
        public async Task Rotate_UserNotFound_ReturnsUnauthorized()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync(SerializeCache(new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1" }));
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("c1"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "c1", UseTokensCookie = false });
            _authRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync((User)null!);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauth.Value.Should().BeEquivalentTo(new { error = "invalid_user" });
        }

        [Fact]
        public async Task Rotate_UserLocked_Returns423()
        {
            SetupClient();
            var cache = new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1" };
            SetupValidRefreshToken(cache, new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10) });

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        // ---------- token issuance outcome ----------

        [Fact]
        public async Task Rotate_InnerReturnsError_ReturnsObjectResultWithStatus()
        {
            SetupClient();
            SetupValidRefreshToken(
                new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1", OrganizationId = "default" },
                new User { ItemId = "u1" });
            // Inner RefreshTokenAuthenticationService fails to resolve tenant -> server_error / 500.
            _innerTenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task Rotate_Success_NonCookieClient_ReturnsTokensInBody()
        {
            SetupClient();
            SetupValidRefreshToken(
                new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1", OrganizationId = "default" },
                new User { ItemId = "u1" });
            SetupSuccessfulInner();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().NotBeNull();
            // cookie_set should be false for a non-cookie client.
            var json = JsonSerializer.Serialize(ok.Value);
            json.Should().Contain("\"refresh_token\":\"new-refresh-token\"");
            json.Should().Contain("\"cookie_set\":false");
        }

        [Fact]
        public async Task Rotate_Success_Impersonated_DelegatesToImpersonation()
        {
            SetupClient();
            SetupValidRefreshToken(
                new RefreshTokenCache
                {
                    UserId = "u1",
                    ClientId = "c1",
                    TenantId = "tenant-1",
                    OrganizationId = "default",
                    Impersonated = true,
                    ImpersonationId = "imp-1"
                },
                new User { ItemId = "u1" });
            SetupSuccessfulInner();

            _authRepo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", TargetTenantId = "target-tenant", UserId = "u1" });
            var sentinel = new OkObjectResult(new { impersonated = true });
            _authService.Setup(s => s.ExecuteImpersonateAsync(It.IsAny<Authentication.DomainService.Shared.RequestModel.ImpersonateRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(sentinel);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeSameAs(sentinel);
            _authService.Verify(s => s.ExecuteImpersonateAsync(It.IsAny<Authentication.DomainService.Shared.RequestModel.ImpersonateRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()), Times.Once);
        }
    }
}
