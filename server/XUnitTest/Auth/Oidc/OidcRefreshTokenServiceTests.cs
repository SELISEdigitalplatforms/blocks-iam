using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Shared.Services;
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
        private readonly Mock<IRefreshSessionResolver> _sessionResolver = new();

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
            new(_authRepo.Object, _cache.Object, _tenants.Object, BuildInner(), _authService.Object, _refreshTokenRepo.Object, _sessionResolver.Object, NullLogger<OidcRefreshTokenService>.Instance);

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
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>())).ReturnsAsync(tokenCache);
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
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>())).ReturnsAsync((RefreshTokenCache?)null);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_grant", error_description = "Refresh token is invalid or expired" });
        }

        [Fact]
        public async Task Rotate_TokenCacheEmptyUserId_ReturnsInvalidGrant()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new RefreshTokenCache { UserId = "", ClientId = "c1", TenantId = "tenant-1" });

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Rotate_ClientRegistrationForTokenMissing_ReturnsUnauthorizedInvalidClient()
        {
            SetupClient();
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            // The token's client id resolves to no registration.
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new RefreshTokenCache { UserId = "u1", ClientId = "missing-client", TenantId = "tenant-1" });
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
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "other-tenant" });
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
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1" });
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

        // ---------- impersonated refresh: one rotation, never a root intermediate ----------
        //
        // The refresh used to rotate the lineage into a plain root token pair and only then call
        // ExecuteImpersonateAsync to rotate that into an impersonated one. The root intermediate was
        // persisted unrevoked while the impersonated predecessor was revoked pointing at it, so the
        // grace-window chain walk in RefreshSessionResolver could hand a concurrent refresh a root
        // token -- permanently, if the second rotation failed. These tests pin the shape that makes
        // that impossible: impersonation is resolved BEFORE the rotation, every rejection happens
        // before anything is written, and the single rotation is impersonated end to end.

        private const string TargetTenantId = "target-tenant";

        /// <summary>Captures every TokenRequest the mint is asked to issue from.</summary>
        private List<TokenRequest> CaptureMintedRequests()
        {
            var minted = new List<TokenRequest>();
            _innerTenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(RtaTests.MakeTenant());
            _innerJwt.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(RtaTests.MakeJwtAccessToken());
            _innerTokenMgr.Setup(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()))
                .Callback<TokenRequest, JwtAccessToken, IdentityConfiguration, Tenant, User>((req, _, _, _, _) => minted.Add(req))
                .ReturnsAsync(("new-refresh-token", DateTime.UtcNow.AddMinutes(30)));
            return minted;
        }

        private static RefreshTokenCache ImpersonatedCache(string? impersonationId = "imp-1") => new()
        {
            RefreshToken = "rt-1",
            UserId = "u1",
            ClientId = "c1",
            TenantId = "tenant-1",
            OrganizationId = "default",
            SessionId = "idp-session-1",
            Impersonated = true,
            ImpersonationId = impersonationId
        };

        /// <summary>Wires the happy path of the pre-rotation resolution: active session, live target tenant, share intact.</summary>
        private void SetupRestorableImpersonation(string status = "active", bool shared = true, bool targetTenantResolves = true)
        {
            _authRepo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1"))
                .ReturnsAsync(new ImpersonationSession { Id = "imp-1", UserId = "u1", TargetTenantId = TargetTenantId, RootTenantId = "tenant-1", Status = status });
            _tenants.Setup(t => t.GetTenantByID(TargetTenantId)).Returns(targetTenantResolves ? RtaTests.MakeTenant() : null!);
            _authService.Setup(s => s.IsTenantSharedWithUserAsync("u1", TargetTenantId)).ReturnsAsync(shared);
        }

        private void VerifyNothingWasMinted() =>
            _innerTokenMgr.Verify(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()), Times.Never);

        [Fact]
        public async Task Rotate_Impersonated_IssuesOneImpersonatedRotation_AndNeverDelegatesToImpersonate()
        {
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            SetupRestorableImpersonation();
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<OkObjectResult>();

            minted.Should().ContainSingle();
            var request = minted[0];
            request.IsImpersonation.Should().BeTrue();
            request.TargetTenantId.Should().Be(TargetTenantId);
            request.OriginalTenantId.Should().Be("tenant-1");
            request.ImpersonationSessionId.Should().Be("imp-1");

            // The detour that created the root intermediate is gone entirely.
            _authService.Verify(s => s.ExecuteImpersonateAsync(
                It.IsAny<Authentication.DomainService.Shared.RequestModel.ImpersonateRequest>(),
                It.IsAny<HttpRequest>(),
                It.IsAny<HttpResponse>()), Times.Never);
        }

        [Fact]
        public async Task Rotate_Impersonated_NeverMintsANonImpersonatedToken()
        {
            // The regression itself: not "the final token is impersonated", but "no token issued at any
            // point in this refresh is root-scoped". A root token persisted mid-flight is reachable
            // through the predecessor's SupersededByTokenId pointer even when it is never returned.
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            SetupRestorableImpersonation();
            var minted = CaptureMintedRequests();

            await Create().RotateAsync(MakeRequest(FormWithToken()));

            minted.Should().NotBeEmpty();
            minted.Should().OnlyContain(r => r.IsImpersonation);
        }

        [Fact]
        public async Task Rotate_Impersonated_GraceReplay_StaysImpersonated()
        {
            // A replay resolves onto the successor. That successor is impersonated, so the replay must
            // re-derive impersonation from it rather than falling back to a root mint.
            SetupClient();
            var successor = ImpersonatedCache();
            successor.RefreshToken = "successor-rt";
            successor.AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(6);
            SetupValidRefreshToken(successor, new User { ItemId = "u1" });
            SetupRestorableImpersonation();
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<OkObjectResult>();
            minted.Should().ContainSingle();
            minted[0].GraceReplayTokenId.Should().Be("successor-rt");
            minted[0].IsImpersonation.Should().BeTrue();
        }

        [Fact]
        public async Task Rotate_Impersonated_SessionMissing_RejectsBeforeRotating()
        {
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            _authRepo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1")).ReturnsAsync((ImpersonationSession?)null);
            CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            // Previously this dereferenced the missing session and threw after the root token had
            // already been written.
            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_grant", error_description = "Impersonation session is no longer active" });
            VerifyNothingWasMinted();
        }

        [Fact]
        public async Task Rotate_Impersonated_SessionEnded_RejectsBeforeRotating()
        {
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            SetupRestorableImpersonation(status: "ended_by_admin_stop");
            CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<BadRequestObjectResult>();
            VerifyNothingWasMinted();
        }

        [Fact]
        public async Task Rotate_Impersonated_WithoutSessionId_RejectsBeforeRotating()
        {
            // Impersonated with no session named: the target tenant is unknowable, and the old code
            // fell through to the root-cookie branch, which is the leak in its purest form.
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(impersonationId: null), new User { ItemId = "u1" });
            CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_grant", error_description = "Impersonated session cannot be restored" });
            VerifyNothingWasMinted();
        }

        [Fact]
        public async Task Rotate_Impersonated_TargetTenantUnresolvable_RejectsBeforeRotating()
        {
            // A tenant-cache miss used to surface as a failed second rotation, stranding the root token
            // at the head of the lineage for the whole grace window.
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            SetupRestorableImpersonation(targetTenantResolves: false);
            CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_target_tenant", error_description = "Target tenant does not exist" });
            VerifyNothingWasMinted();
        }

        [Fact]
        public async Task Rotate_Impersonated_ShareRevoked_Returns403BeforeRotating()
        {
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            SetupRestorableImpersonation(shared: false);
            CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            VerifyNothingWasMinted();
        }

        [Fact]
        public async Task Rotate_Impersonated_SessionLookupThrows_Returns503BeforeRotating()
        {
            // A store outage is not a licence to de-impersonate.
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            _authRepo.Setup(r => r.GetImpersonationSessionByIdAsync("imp-1")).ThrowsAsync(new TimeoutException("mongo down"));
            CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            VerifyNothingWasMinted();
        }

        [Fact]
        public async Task Rotate_Impersonated_Success_StampsSessionActivity()
        {
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            SetupRestorableImpersonation();
            CaptureMintedRequests();

            await Create().RotateAsync(MakeRequest(FormWithToken()));

            _authRepo.Verify(r => r.UpdateImpersonationSessionAsync("imp-1",
                It.Is<Dictionary<string, object>>(u => u.ContainsKey("LastActivity") && u.ContainsKey("OrganizationId"))), Times.Once);
        }

        [Fact]
        public async Task Rotate_Impersonated_SessionStampFailure_DoesNotFailTheRefresh()
        {
            // The tokens are already issued by this point; bookkeeping must not turn a completed
            // refresh into an error the client answers by logging out.
            SetupClient();
            SetupValidRefreshToken(ImpersonatedCache(), new User { ItemId = "u1" });
            SetupRestorableImpersonation();
            CaptureMintedRequests();
            _authRepo.Setup(r => r.UpdateImpersonationSessionAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .ThrowsAsync(new TimeoutException("mongo down"));

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Rotate_NotImpersonated_SkipsImpersonationWorkEntirely()
        {
            SetupClient();
            SetupValidRefreshToken(
                new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1", OrganizationId = "default" },
                new User { ItemId = "u1" });
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<OkObjectResult>();
            minted.Should().ContainSingle();
            minted[0].IsImpersonation.Should().BeFalse();
            minted[0].TargetTenantId.Should().BeNull();
            _authRepo.Verify(r => r.GetImpersonationSessionByIdAsync(It.IsAny<string>()), Times.Never);
            _authService.Verify(s => s.IsTenantSharedWithUserAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _authRepo.Verify(r => r.UpdateImpersonationSessionAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
        }

    }
}
