using System.Security.Claims;
using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Authentication.LoginPolicies;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.Services;
using Authentication.DomainService.SignupLinks;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestDoubles;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 C1 per explicit source (switch target, OAuth refresh hint, client-sent sign-in organization via
    /// biometric / SSO consent, signup link), C2 and C10, and the password pre-fill that stays a preference (H1).
    /// The token refresher is stood in by the real <see cref="JwtAccessTokenProvider.ResolveOrganizationAsync"/>,
    /// which is exactly what <c>ManageTokenAsync</c> runs first.
    /// </summary>
    public sealed class ExplicitOrganizationSourceTests : IDisposable
    {
        private const string TenantId = "tenant-1";
        private const string A = "org-a";
        private const string D = "org-d";
        private const string DisabledMessage = "Your organization is disabled. Please contact your administrator or support.";

        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IAuthStrategy> _strategy = new();
        private readonly Mock<ITokenRefresher> _refresher = new();
        private readonly Mock<Authentication.DomainService.Authentication.IAuthenticationService> _authService = new();
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private readonly Mock<IRefreshSessionResolver> _sessionResolver = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IResourceRepository> _resources = new();
        private readonly List<TokenRequest> _issued = [];

        public ExplicitOrganizationSourceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));

            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _authService.Setup(a => a.CookieToken(It.IsAny<HttpRequest>())).Returns("rt");
            _sessionResolver
                .Setup(r => r.ResolveRefreshSessionAsync(It.IsAny<string>(), It.IsAny<IdentityConfiguration>(), It.IsAny<bool>()))
                .Returns(async (string token, IdentityConfiguration configuration, bool _) =>
                    new RefreshSessionResolution(await _sessionResolver.Object.TryResolveRefreshSessionAsync(token, configuration), false));
            _resources.MultiOrg();
            _resources.WithOrganizationStatuses(disabled: [D]);

            // The refresher runs the organization gate ManageTokenAsync runs first, and "mints" on success.
            var provider = DisabledOrganizationsKit.RealProvider(_resources);
            _refresher.Setup(r => r.AuthenticateAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>()))
                .Returns(async (TokenRequest request, IdentityConfiguration _, User user) =>
                {
                    _issued.Add(request);
                    return await provider.ResolveOrganizationAsync(user, request)
                        ?? new TokenResponse { AccessToken = "at", RefreshToken = "new-rt", StatusCode = 200 };
                });
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationFlowService Flow() =>
            new(_repo.Object, _strategy.Object, _refresher.Object, _authService.Object, new Mock<ICaptchaEvaluator>().Object,
                new OidcLoginAuditWriter(_authDomain.Object, _activity.Object, NullLogger<OidcLoginAuditWriter>.Instance),
                NullLogger<AuthenticationFlowService>.Instance, _refreshRepo.Object, _sessionResolver.Object,
                UnknownLoginGuardStub.Create().Object, new FakeLoginPolicy());

        private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(new[]
        {
            new Claim(BlocksContext.TENANT_ID_CLAIM, TenantId),
            new Claim(BlocksContext.USER_ID_CLAIM, "user-1")
        }));

        private void Session(string? organizationHint) =>
            _sessionResolver.Setup(r => r.TryResolveRefreshSessionAsync("rt", It.IsAny<IdentityConfiguration>()))
                .ReturnsAsync(new RefreshTokenCache
                {
                    RefreshToken = "rt", UserId = "user-1", TenantId = TenantId, OrganizationId = organizationHint,
                    ClientId = "client-1", SessionId = "session-1", RefreshTokenSessionId = "lineage-1",
                    IssuedUtc = DateTime.UtcNow.AddHours(-2), ExpiresUtc = DateTime.UtcNow.AddMinutes(30), AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(6)
                });

        private void UserIs(User user) => _repo.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(user);

        private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

        private void VerifyLastUsedUnchanged() =>
            _repo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);

        // ---------- switch-org (§7 example 3) ----------

        [Fact]
        public async Task C1_SwitchTarget_Disabled_Returns403_LastUsedUnchanged()
        {
            UserIs(new User { ItemId = "user-1", OrganizationIds = [A, D], LastUsedOrganizationId = A });
            Session(A);

            var result = await Flow().ExecuteSwitchOrganizationAsync(new SwitchOrganizationRequest { OrganizationId = D }, Principal(), new DefaultHttpContext().Request);

            result.TokenResponse!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            result.TokenResponse.Error.Should().Be("organization_disabled");
            result.TokenResponse.ErrorDescription.Should().Be(DisabledMessage);
            result.TokenResponse.AccessToken.Should().BeNull();
            _issued.Should().ContainSingle().Which.OrganizationSource.Should().Be(OrganizationRequestSource.Explicit);
            VerifyLastUsedUnchanged();
        }

        [Fact]
        public async Task C1_SwitchTarget_NonMember_Stays400_OrganizationNotAvailable_WithoutStatusRead()
        {
            UserIs(new User { ItemId = "user-1", OrganizationIds = [A], LastUsedOrganizationId = A });
            Session(A);

            var result = await Flow().ExecuteSwitchOrganizationAsync(new SwitchOrganizationRequest { OrganizationId = "not-mine" }, Principal(), new DefaultHttpContext().Request);

            result.Error.Should().Be("organization_not_available");
            _issued.Should().BeEmpty();
            _resources.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }

        [Fact]
        public async Task H2_H7_SwitchTarget_Active_Issued_AndPersistedAsLastUsed()
        {
            UserIs(new User { ItemId = "user-1", OrganizationIds = [A, "org-b"], LastUsedOrganizationId = A });
            Session(A);

            var result = await Flow().ExecuteSwitchOrganizationAsync(new SwitchOrganizationRequest { OrganizationId = "org-b" }, Principal(), new DefaultHttpContext().Request);

            result.TokenResponse!.AccessToken.Should().Be("at");
            _issued.Single().OrganizationId.Should().Be("org-b");
            _repo.Verify(r => r.UpdatePartialAsync<User>("user-1",
                It.Is<Dictionary<string, object>>(d => (string)d[nameof(User.LastUsedOrganizationId)] == "org-b"), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task C11_SwitchIntoHeldDefault_StillWorks_WithoutStatusRead()
        {
            UserIs(new User { ItemId = "user-1", OrganizationIds = [IdpConstants.DefaultOrganizationId] });
            Session(IdpConstants.DefaultOrganizationId);

            var result = await Flow().ExecuteSwitchOrganizationAsync(
                new SwitchOrganizationRequest { OrganizationId = IdpConstants.DefaultOrganizationId }, Principal(), new DefaultHttpContext().Request);

            result.TokenResponse!.AccessToken.Should().Be("at");
            _issued.Single().OrganizationDecision!.Scope.ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
            _resources.Verify(r => r.GetOrganizationStatusesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }

        // ---------- auth/refresh (§7 example 2, C2) ----------

        [Fact]
        public async Task C1_C2_OAuthRefreshHint_Disabled_403_LineageNotRevoked_ThenPlainSignInLandsInA()
        {
            var user = new User { ItemId = "user-1", OrganizationIds = [D, A], LastUsedOrganizationId = D };
            UserIs(user);
            Session(D);
            var ctx = new DefaultHttpContext();

            var result = await Flow().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            var refusal = result.Should().BeOfType<ObjectResult>().Subject;
            refusal.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            Prop(refusal.Value, "error").Should().Be("organization_disabled");
            Prop(refusal.Value, "error_description").Should().Be(DisabledMessage);
            _issued.Single().OrganizationSource.Should().Be(OrganizationRequestSource.Explicit);
            _refreshRepo.Verify(r => r.RevokeLineageAndEvictAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            VerifyLastUsedUnchanged();

            // C2: the following plain sign-in (server-filled pre-fill) lands in the active organization.
            var signIn = new TokenRequest { GrantType = GrantTypes.Social, OrganizationId = user.LastUsedOrganizationId };
            (await DisabledOrganizationsKit.RealProvider(_resources).ResolveOrganizationAsync(user, signIn)).Should().BeNull();
            signIn.OrganizationId.Should().Be(A);
        }

        [Fact]
        public async Task OAuthRefresh_BlankHint_IsTreatedAsNone()
        {
            UserIs(new User { ItemId = "user-1", OrganizationIds = [D, A] });
            Session(null);
            _refresher.Setup(r => r.GetTenantByIDAsync(It.IsAny<string>())).ReturnsAsync((Tenant?)null);
            var ctx = new DefaultHttpContext();

            var result = await Flow().ExecuteRefreshAsync(new RefreshRequest { RefreshToken = "rt" }, Principal(), ctx.Request, ctx.Response);

            result.Should().BeOfType<OkObjectResult>();
            _issued.Single().OrganizationId.Should().Be(A);
        }

        // ---------- client-sent sign-in organization (biometric, SSO consent) ----------

        [Fact]
        public async Task C1_ClientSentOrganization_Biometric_IsExplicit()
        {
            var tokenManager = new Mock<IOAuthJwtAccessTokenManager>();
            tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .ReturnsAsync(JwtAccessTokenProvider.OrganizationRefusal(OrganizationIssuanceOutcome.OrganizationDisabled));
            _repo.Setup(r => r.AuthenticateBiometricCredentialAsync("bio-id", "bio-key")).ReturnsAsync(new BiometricCredential { UserId = "user-1" });
            UserIs(new User { ItemId = "user-1", Active = true, OrganizationIds = [D, A] });

            var result = await new BiometricAuthorizationService(_repo.Object, tokenManager.Object)
                .AuthenticateAsync(new TokenRequest { BiometricId = "bio-id", BiometricKey = "bio-key", OrganizationId = D }, new IdentityConfiguration());

            result.StatusCode.Should().Be(403);
            tokenManager.Verify(m => m.ManageTokenAsync(
                It.Is<TokenRequest>(t => t.OrganizationSource == OrganizationRequestSource.Explicit && t.OrganizationId == D),
                It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()), Times.Once);
        }

        [Fact]
        public async Task C1_ClientSentOrganization_ExplicitDisabled_RefusedByTheGate()
        {
            var user = new User { ItemId = "user-1", OrganizationIds = [D, A], LastUsedOrganizationId = A };
            var request = new TokenRequest { GrantType = GrantTypes.BiometricAuthorization, OrganizationId = D, OrganizationSource = OrganizationRequestSource.Explicit };

            var refusal = await DisabledOrganizationsKit.RealProvider(_resources).ResolveOrganizationAsync(user, request);

            refusal!.StatusCode.Should().Be(403);
            refusal.Error.Should().Be("organization_disabled");
        }

        [Fact]
        public async Task C1_ClientSentOrganization_SsoConsent_IsExplicit()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.KeyExistsAsync("sso-code")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("sso-code"))
                .ReturnsAsync(JsonSerializer.Serialize(new CreateUserViaSsoRequest { Email = "sso@user.com", Platform = "google" }));
            var userMutation = new Mock<IUserManagementMutationService>();
            userMutation.Setup(u => u.CreateUserFromSsoAsync(It.IsAny<CreateUserViaSsoRequest>()))
                .ReturnsAsync(new BaseMutationResponse { ItemId = "user-1", IsSuccess = true });
            UserIs(new User { ItemId = "user-1", Active = true });
            var tokenManager = new Mock<IOAuthJwtAccessTokenManager>();
            tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "at" });

            await new SSOConsentAuthenticationService(cache.Object, _repo.Object, tokenManager.Object, userMutation.Object)
                .AuthenticateAsync(new TokenRequest { Code = "sso-code", OrganizationId = A }, new IdentityConfiguration());

            tokenManager.Verify(m => m.ManageTokenAsync(
                It.Is<TokenRequest>(t => t.OrganizationSource == OrganizationRequestSource.Explicit),
                It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()), Times.Once);
        }

        // ---------- signup link ----------

        [Fact]
        public async Task C1_SignupLink_SetsLinkSource_AndARefusalIsWhatTheConstructReceives()
        {
            var tokenManager = new Mock<IOAuthJwtAccessTokenManager>();
            var refusal = JwtAccessTokenProvider.OrganizationRefusal(OrganizationIssuanceOutcome.OrganizationDisabled);
            tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .ReturnsAsync(refusal);
            _authService.Setup(a => a.BuildFlowResultAsync(It.IsAny<AuthenticationFlowResult>(), It.IsAny<HttpContext>()))
                .ReturnsAsync(new ObjectResult("refused") { StatusCode = 403 });
            var issuer = new SignupLinkEmbeddedTokenIssuer(_repo.Object, tokenManager.Object, _authService.Object, NullLogger<SignupLinkEmbeddedTokenIssuer>.Instance);

            var result = await issuer.IssueAsync(new SignupLink { OrganizationId = D }, new User { ItemId = "user-1" }, ["link"], new DefaultHttpContext().Request);

            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
            tokenManager.Verify(m => m.ManageTokenAsync(
                It.Is<TokenRequest>(t => t.OrganizationSource == OrganizationRequestSource.Link && t.OrganizationId == D),
                It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()), Times.Once);
            _authService.Verify(a => a.BuildFlowResultAsync(It.Is<AuthenticationFlowResult>(f => f.TokenResponse == refusal), It.IsAny<HttpContext>()), Times.Once);
        }

        // ---------- password pre-fill stays a preference; refusals keep last-used (C10) ----------

        private static PasswordAuthenticationService Password(Mock<IAuthenticationRepository> repo, Mock<IOAuthJwtAccessTokenManager> tokenManager) =>
            new(NullLogger<PasswordAuthenticationService>.Instance, tokenManager.Object, new Mock<ICryptoService>().Object, repo.Object,
                new PasswordCredentialVerifier(NullLogger<PasswordCredentialVerifier>.Instance, new Mock<ITenants>().Object, repo.Object,
                    new Mock<IAccountService>().Object, new Mock<IUserActivityDispatcher>().Object, new Mock<IAuthenticationDomainService>().Object),
                UnknownLoginGuardStub.Create().Object);

        private static Mock<IAuthenticationRepository> PasswordRepo(User user)
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetUserByUsernameAsync("jane", It.IsAny<string?>())).ReturnsAsync(user);
            repo.Setup(r => r.TryReserveLoginAttemptAsync(user.ItemId, It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = user.ItemId, FailedLoginCount = 1 });
            repo.Setup(r => r.ReleaseLoginAttemptAsync(user.ItemId, It.IsAny<DateTime>())).ReturnsAsync(true);
            return repo;
        }

        private static User PasswordUser(string? lastUsed) => new()
        {
            ItemId = "u1", Active = true, IsVerified = true, Password = BCrypt.Net.BCrypt.HashPassword("secret", 4),
            OrganizationIds = [D, A], LastUsedOrganizationId = lastUsed
        };

        [Fact]
        public async Task C10_PasswordLogin_Refused_DoesNotWriteLastUsed_AndPreFillIsServerFilled()
        {
            var repo = PasswordRepo(PasswordUser(lastUsed: D));
            var tokenManager = new Mock<IOAuthJwtAccessTokenManager>();
            tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .ReturnsAsync(JwtAccessTokenProvider.OrganizationRefusal(OrganizationIssuanceOutcome.OrganizationDisabled));

            // The password service does not read the grant type; it is left on the social grant on purpose.
            var result = await Password(repo, tokenManager).AuthenticateAsync(
                new TokenRequest { GrantType = GrantTypes.Social, Username = "jane", Password = "secret" }, new IdentityConfiguration());

            result.StatusCode.Should().Be(403);
            tokenManager.Verify(m => m.ManageTokenAsync(
                It.Is<TokenRequest>(t => t.OrganizationSource == OrganizationRequestSource.ServerFilled),
                It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()), Times.Once);
            repo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(),
                It.Is<Dictionary<string, object>>(d => d.ContainsKey(nameof(User.LastUsedOrganizationId))), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Q1_PasswordLogin_LastUsedFollowsThePreFill_NotTheWrittenBackOrganization()
        {
            // The mint wrote A back to request.OrganizationId (Q1); last-used keeps following the pre-fill.
            var repo = PasswordRepo(PasswordUser(lastUsed: null));
            var tokenManager = new Mock<IOAuthJwtAccessTokenManager>();
            tokenManager.Setup(m => m.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .Callback((TokenRequest request, IdentityConfiguration _, User _, StateInfo? _) => request.OrganizationId = A)
                .ReturnsAsync(new TokenResponse { AccessToken = "tok" });

            await Password(repo, tokenManager).AuthenticateAsync(
                new TokenRequest { GrantType = GrantTypes.Social, Username = "jane", Password = "secret", OrganizationId = D }, new IdentityConfiguration());

            repo.Verify(r => r.UpdatePartialAsync<User>("u1",
                It.Is<Dictionary<string, object>>(d => d.ContainsKey(nameof(User.LastUsedOrganizationId)) && (string)d[nameof(User.LastUsedOrganizationId)] == D),
                It.IsAny<string>()), Times.Once);
        }
    }
}
