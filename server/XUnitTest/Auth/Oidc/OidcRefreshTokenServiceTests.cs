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
using Blocks.Genesis.Entities.Iam;
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
            new(_authRepo.Object, _tenants.Object, BuildInner(), _authService.Object, _refreshTokenRepo.Object, _sessionResolver.Object, NullLogger<OidcRefreshTokenService>.Instance);

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

        // ---------- account state: a deactivated or suspended user cannot refresh ----------

        public static TheoryData<User> BlockedUsers => new()
        {
            // What DeactivateUserAsync writes today.
            new User { ItemId = "u1", Active = false, IsVerified = true, Status = UserLifecycleStatus.Disabled, DeactivatedAtUtc = DateTime.UtcNow },
            // Status alone, even with the flag left on.
            new User { ItemId = "u1", Active = true, IsVerified = true, Status = UserLifecycleStatus.Disabled },
            new User { ItemId = "u1", Active = true, IsVerified = true, Status = UserLifecycleStatus.Suspended },
            // Deactivated before Status existed: verified once, now inactive.
            new User { ItemId = "u1", Active = false, IsVerified = true, Status = UserLifecycleStatus.Active },
        };

        [Theory]
        [MemberData(nameof(BlockedUsers))]
        public async Task Rotate_BlockedUser_IsRefusedInvalidGrant_RevokesTheLineage_AndMintsNothing(User user)
        {
            SetupClient();
            var cache = new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1", RefreshToken = "rt-1", RefreshTokenSessionId = "lineage-1" };
            SetupValidRefreshToken(cache, user);
            SetupSuccessfulInner();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_grant", error_description = "Refresh token is invalid or expired" });
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync("lineage-1", BlockedUserRefreshGuard.RevokeReason), Times.Once);
            _innerTokenMgr.Verify(m => m.ManageRefreshTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<JwtAccessToken>(), It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task Rotate_BlockedUser_LineageRevokeFails_IsStillRefused()
        {
            SetupClient();
            var cache = new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1", RefreshToken = "rt-1" };
            SetupValidRefreshToken(cache, new User { ItemId = "u1", Active = false, IsVerified = true, Status = UserLifecycleStatus.Disabled });
            _refreshTokenRepo.Setup(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("store down"));

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<BadRequestObjectResult>();
            // No lineage id on the cache entry: the presented token id is the lineage root.
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync("rt-1", BlockedUserRefreshGuard.RevokeReason), Times.Once);
        }

        public static TheoryData<User> UsableUsers => new()
        {
            new User { ItemId = "u1", Active = true, IsVerified = true, Status = UserLifecycleStatus.Active },
            // Never activated (invite / signup link): may hold a session, must keep refreshing.
            new User { ItemId = "u1", Active = false, IsVerified = false, Status = UserLifecycleStatus.PendingVerification },
            new User { ItemId = "u1", Active = false, IsVerified = false, Status = UserLifecycleStatus.Active },
            // A document written before Status existed and later read with the enum's first value.
            new User { ItemId = "u1", Active = true, IsVerified = true, Status = UserLifecycleStatus.PendingVerification },
        };

        [Theory]
        [MemberData(nameof(UsableUsers))]
        public async Task Rotate_UsableUser_StillRotates(User user)
        {
            SetupClient();
            SetupValidRefreshToken(
                new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1", OrganizationId = "default" },
                user);
            SetupSuccessfulInner();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<OkObjectResult>();
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Rotate_IssuesTheAccessToken_FromThePersistedUser_NotTheRefreshRecordsVersion()
        {
            // The refresh record keeps the synthetic TokenVersion = 1 from the code exchange; the new
            // access token's token_version / security_stamp must come from the user as stored now.
            SetupClient();
            SetupValidRefreshToken(
                new RefreshTokenCache { UserId = "u1", ClientId = "c1", TenantId = "tenant-1", OrganizationId = "default", TokenVersion = 1 },
                new User { ItemId = "u1", Active = true, IsVerified = true, Status = UserLifecycleStatus.Active, TokenVersion = 4, SecurityStamp = "S4" });
            SetupSuccessfulInner();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken()));

            result.Should().BeOfType<OkObjectResult>();
            _innerJwt.Verify(p => p.GetJwtAccessToken(
                It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(),
                It.Is<User>(u => u.TokenVersion == 4 && u.SecurityStamp == "S4"),
                It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()), Times.Once);
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

        // ---------- blocks-iam#624: /oidc/token accepts a lineage rotated by switch-org or /auth/refresh ----------
        //
        // The successor below is produced by the real rotation rule from a hosted-OIDC predecessor whose
        // client is "client-1", using the TokenRequest shape each endpoint builds -- including the
        // pre-fix shape that named no client -- and is then presented to /oidc/token.

        public static TheoryData<string, string?, string> EndpointRotations => new()
        {
            { GrantTypes.SwitchOrganization, "client-1", "org-b" },   // H3: switch-org, as fixed
            { GrantTypes.SwitchOrganization, null, "org-b" },         // H3/H10: even a caller naming no client
            { GrantTypes.RefreshToken, "client-1", "default" },       // H5: /auth/refresh, as fixed
            { GrantTypes.RefreshToken, null, "default" },             // H5/H10
        };

        [Theory]
        [MemberData(nameof(EndpointRotations))]
        public async Task Rotate_LineageRotatedBySwitchOrgOrAuthRefresh_PassesTheClientCheckAndReturns200(
            string grantType, string? requestedClient, string organizationId)
        {
            var (stored, successor) = await XUnitTest.Auth.Shared.RefreshTokenLineageTests.RotateOnceAsync(
                XUnitTest.Auth.Shared.RefreshTokenLineageTests.HostedPredecessor(),
                new TokenRequest { GrantType = grantType, ClientId = requestedClient, OrganizationId = organizationId });
            stored.ClientId.Should().Be("client-1");
            successor.ClientId.Should().Be("client-1");

            SetupClient("client-1");
            SetupValidRefreshToken(successor, new User { ItemId = successor.UserId! });
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("client-1", successor.RefreshToken!)));

            result.Should().BeOfType<OkObjectResult>("the lineage still names a registered client");
            minted.Should().ContainSingle().Which.ClientId.Should().Be("client-1");
        }

        [Fact]
        public async Task Rotate_LineageWhoseClientMatchesTheRequest_RotatesAgainWithTheSameClientAndNoMismatch()
        {
            // H9: /oidc/token -> /oidc/token. The request the endpoint mints from names the stored
            // client, which the rotation rule keeps as-is (the no-warning half is asserted by
            // RefreshTokenLineageTests.Rotation_ResolvesTheSuccessorClientFromTheLineageWithoutWarning).
            var predecessor = XUnitTest.Auth.Shared.RefreshTokenLineageTests.HostedPredecessor("rt-1");
            SetupClient("client-1");
            SetupValidRefreshToken(predecessor, new User { ItemId = predecessor.UserId! });
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("client-1", "rt-1")));

            result.Should().BeOfType<OkObjectResult>();
            var request = minted.Should().ContainSingle().Subject;
            request.ClientId.Should().Be("client-1");

            var (stored, successor) = await XUnitTest.Auth.Shared.RefreshTokenLineageTests.RotateOnceAsync(predecessor, request);
            stored.ClientId.Should().Be("client-1");
            successor.ClientId.Should().Be("client-1");
        }

        [Fact]
        public async Task Rotate_ClientLessLineageAfterAuthRefresh_IsStillInvalidClient()
        {
            // C2: an already client-less lineage is not repaired by /auth/refresh, and /oidc/token keeps
            // refusing it exactly as before.
            var predecessor = XUnitTest.Auth.Shared.RefreshTokenLineageTests.HostedPredecessor();
            predecessor.ClientId = string.Empty;
            var (_, successor) = await XUnitTest.Auth.Shared.RefreshTokenLineageTests.RotateOnceAsync(
                predecessor,
                new TokenRequest { GrantType = GrantTypes.RefreshToken, ClientId = predecessor.ClientId, OrganizationId = "default" });
            successor.ClientId.Should().BeNullOrEmpty();

            SetupClient("client-1");
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(successor);

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("client-1", successor.RefreshToken!)));

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauth.Value.Should().BeEquivalentTo(new { error = "invalid_client", error_description = "Client configuration not found" });
            VerifyNothingWasMinted();
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

            // ---------- the token is redeemable only by the client it was issued to ----------

        private void SetupLineage(string lineageClientId)
        {
            SetupValidRefreshToken(
                new RefreshTokenCache { UserId = "u1", ClientId = lineageClientId, TenantId = "tenant-1", OrganizationId = "default", RefreshToken = "rt-1", RefreshTokenSessionId = "lineage-1" },
                new User { ItemId = "u1" });
        }

        [Fact]
        public async Task Rotate_RequestClientMatchesLineageClient_Rotates()
        {
            SetupClient("c1");
            SetupLineage("c1");
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("c1")));

            result.Should().BeOfType<OkObjectResult>();
            minted.Should().ContainSingle().Which.ClientId.Should().Be("c1");
        }

        [Fact]
        public async Task Rotate_RequestClientDiffersFromLineageClient_ReturnsInvalidGrant_AndMintsNothing()
        {
            SetupClient("c2");
            SetupClient("c1");
            SetupLineage("c1");
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("c2")));

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            bad.Value.Should().BeEquivalentTo(new { error = "invalid_grant", error_description = "Refresh token was not issued to this client" });
            minted.Should().BeEmpty();
            _refreshTokenRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Rotate_ClientIdComparison_IsCaseSensitive()
        {
            SetupClient("C1");
            SetupClient("c1");
            SetupLineage("c1");
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("C1")));

            result.Should().BeOfType<BadRequestObjectResult>();
            minted.Should().BeEmpty();
        }

        [Fact]
        public async Task Rotate_PublicClientWithoutSecret_MatchingLineage_Rotates()
        {
            SetupValidRefreshToken(
                new RefreshTokenCache { UserId = "u1", ClientId = "spa", TenantId = "tenant-1", OrganizationId = "default", RefreshToken = "rt-1" },
                new User { ItemId = "u1" });
            // A public client has no secret to send; it only has to be the token's client.
            _authRepo.Setup(r => r.GetOidcClientRegistrationAsync("spa"))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "spa", ClientType = "public", TokenEndpointAuthMethod = "none", UseTokensCookie = false });
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("spa")));

            result.Should().BeOfType<OkObjectResult>();
            minted.Should().ContainSingle();
        }

        [Fact]
        public async Task Rotate_LineageWithoutClient_KeepsTheInvalidClientAnswer()
        {
            // Unchanged behavior: a client-less lineage was refused before the binding check existed.
            SetupClient("c1");
            _authRepo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new RefreshTokenCache { UserId = "u1", ClientId = "", TenantId = "tenant-1" });
            var minted = CaptureMintedRequests();

            var result = await Create().RotateAsync(MakeRequest(FormWithToken("c1")));

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauth.Value.Should().BeEquivalentTo(new { error = "invalid_client", error_description = "Client configuration not found" });
            minted.Should().BeEmpty();
        }
    }
}
