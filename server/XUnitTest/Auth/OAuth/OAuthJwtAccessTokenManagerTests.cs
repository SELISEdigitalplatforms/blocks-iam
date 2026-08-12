using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IdentityModel.Tokens.Jwt;

namespace XUnitTest.Auth.OAuth
{
    public class OAuthJwtAccessTokenManagerTests : IDisposable
    {
        private readonly Mock<IJwtAccessTokenProvider> _jwtProvider = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IMfaPolicyService> _mfaPolicy = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IOtpServiceFactory> _otpFactory = new();

        // UnifiedTokenSessionService dependencies (concrete service built for real).
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();
        private readonly Mock<IIdpSessionService> _idpSession = new();
        private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
        private readonly Mock<IUserRepository> _userRepository = new();

        public OAuthJwtAccessTokenManagerTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new DeviceInformation());
            _authDomain.Setup(a => a.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "1.1.1.1" });
            _idpSession.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("session-1");
            _refreshRepo.Setup(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>())).ReturnsAsync("id");
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            _userRepository.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(new User { ItemId = "u1" });
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private UnifiedTokenSessionService BuildUnifiedTokenSessionService() =>
            new(_cache.Object, _authDomain.Object, _refreshRepo.Object, _dispatcher.Object, _idpSession.Object,
                _httpContextAccessor.Object, _userRepository.Object, NullLogger<UnifiedTokenSessionService>.Instance);

        private OAuthJwtAccessTokenManager Create() =>
            new(_jwtProvider.Object, _authDomain.Object, _authRepo.Object, _mfaPolicy.Object, _cache.Object,
                _tenants.Object, _otpFactory.Object, BuildUnifiedTokenSessionService());

        private static TokenRequest MakeRequest(string grantType) => new()
        {
            GrantType = grantType,
            ClientId = "client-1",
            OrganizationId = "default",
            Request = new DefaultHttpContext().Request
        };

        private void SetupMfaRequiredPolicy(bool mustEnroll, UserMfaType? preferred = UserMfaType.Email)
        {
            _mfaPolicy.Setup(p => p.EvaluateAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaPolicyDecision
                {
                    Required = true,
                    MustEnrollFirst = mustEnroll,
                    PreferredMethod = preferred,
                    AllowedMethods = new List<UserMfaType> { UserMfaType.Email, UserMfaType.TOTP }
                });
        }

        // ---------- SPEC2 H4 / SPEC1 H3: cookie expiry and grace replay ----------

        [Fact]
        public async Task ManageRefreshToken_NewLineage_ReturnsTheAbsoluteExpiryForTheCookie()
        {
            var request = MakeRequest(GrantTypes.Password);

            var (token, cookieExpiry) = await Create().ManageRefreshTokenAsync(
                request,
                RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken(),
                new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 60, AbsoluteRefreshTokenValidForNumberMinutes = 10080 },
                RefreshTokenAuthenticationServiceTests.MakeTenant(),
                new User { ItemId = "u1" });

            token.Should().NotBeNullOrWhiteSpace();
            // The cookie carries the lineage's absolute cap, not the sliding window.
            cookieExpiry.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10080), TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task ManageRefreshToken_Rotation_KeepsTheCookieExpiryOnTheInheritedCap()
        {
            var cap = DateTime.UtcNow.AddDays(6);
            var oldCache = new Authentication.DomainService.Dtos.RefreshTokenCache
            {
                RefreshToken = "old-token",
                UserId = "u1",
                TenantId = "tenant-1",
                ClientId = "client-1",
                RefreshTokenSessionId = "lineage-1",
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30),
                AbsoluteExpiresUtc = cap
            };
            _cache.Setup(c => c.GetStringValueAsync("old-token"))
                .ReturnsAsync(System.Text.Json.JsonSerializer.Serialize(oldCache));
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            var request = MakeRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "old-token";

            var (_, cookieExpiry) = await Create().ManageRefreshTokenAsync(
                request,
                RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken(),
                new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 60 },
                RefreshTokenAuthenticationServiceTests.MakeTenant(),
                new User { ItemId = "u1" });

            cookieExpiry.Should().Be(cap, "every issuance in one lineage writes the same cookie Expires");
        }

        [Fact]
        public async Task ManageRefreshToken_GraceReplay_ReturnsTheSuccessorAndMintsNothing()
        {
            var cap = DateTime.UtcNow.AddDays(6);
            var request = MakeRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "A";
            request.GraceReplayTokenId = "B";
            request.GraceReplayAbsoluteExpiry = cap;

            var (token, cookieExpiry) = await Create().ManageRefreshTokenAsync(
                request,
                RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken(),
                new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 60 },
                RefreshTokenAuthenticationServiceTests.MakeTenant(),
                new User { ItemId = "u1" });

            token.Should().Be("B");
            cookieExpiry.Should().Be(cap);
            // A replay must not consume another rotation.
            _refreshRepo.Verify(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>()), Times.Never);
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ManageRefreshToken_RotationWithUnresolvablePredecessor_ReturnsEmptyToken()
        {
            _cache.Setup(c => c.GetStringValueAsync("ghost")).ReturnsAsync((string)null!);
            _refreshRepo.Setup(r => r.GetByTokenIdAsync("ghost")).ReturnsAsync((Idp.DomainService.Oidc.Contracts.RefreshTokenModel)null!);

            var request = MakeRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "ghost";

            var (token, _) = await Create().ManageRefreshTokenAsync(
                request,
                RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken(),
                new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 60 },
                RefreshTokenAuthenticationServiceTests.MakeTenant(),
                new User { ItemId = "u1" });

            token.Should().BeEmpty();
            _refreshRepo.Verify(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>()), Times.Never);
        }

        // ---------- CreateJwtAccessToken (static) ----------

        [Fact]
        public void CreateJwtAccessToken_WritesSignedToken()
        {
            var jwtAccessToken = RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken();

            var token = OAuthJwtAccessTokenManager.CreateJwtAccessToken(jwtAccessToken);

            token.Should().NotBeNullOrWhiteSpace();
            var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
            parsed.Issuer.Should().Be("https://issuer");
            parsed.Claims.Should().Contain(c => c.Type == BlocksContext.SUBJECT_CLAIM && c.Value == "blocks|u1");
        }

        // ---------- ManageTokenAsync: MFA checkpoint branches ----------

        [Fact]
        public async Task ManageToken_MfaEnrollmentRequired_Returns403()
        {
            SetupMfaRequiredPolicy(mustEnroll: true);

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.Password), new IdentityConfiguration(), new User { ItemId = "u1" });

            result.Error.Should().Be(OAuthError.MfaEnrollmentRequired);
            result.MfaRequired.Should().BeTrue();
            result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            result.MfaMethods.Should().Contain("Email");
        }

        [Fact]
        public async Task ManageToken_MfaChallenge_NoOtpService_ReturnsServerError()
        {
            SetupMfaRequiredPolicy(mustEnroll: false, preferred: UserMfaType.Email);
            _otpFactory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns((IOtpService)null!);

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.Password), new IdentityConfiguration(), new User { ItemId = "u1" });

            result.Error.Should().Be("server_error");
            result.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task ManageToken_MfaChallenge_EmptyMfaId_ReturnsServerError()
        {
            SetupMfaRequiredPolicy(mustEnroll: false, preferred: UserMfaType.Email);
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>())).ReturnsAsync(new OtpGenerationResponse { MfaId = "" });
            _otpFactory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(otp.Object);

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.Password), new IdentityConfiguration(), new User { ItemId = "u1", Email = "e@x.com" });

            result.Error.Should().Be("server_error");
            result.ErrorDescription.Should().Be("Failed to generate mfa challenge");
            result.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task ManageToken_MfaChallenge_Success_ReturnsMfaEnabled()
        {
            SetupMfaRequiredPolicy(mustEnroll: false, preferred: UserMfaType.Email);
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>())).ReturnsAsync(new OtpGenerationResponse { MfaId = "mfa-123" });
            _otpFactory.Setup(f => f.GetOTPService(UserMfaType.Email)).Returns(otp.Object);

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.Password), new IdentityConfiguration(), new User { ItemId = "u1", Email = "e@x.com" });

            result.Error.Should().Be(OAuthError.MfaEnabled);
            result.MfaId.Should().Be("mfa-123");
            result.UserMfa.Should().Be(UserMfaType.Email);
            result.MfaRequired.Should().BeTrue();
            result.ClientId.Should().Be("client-1");
            result.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task ManageToken_MfaChallenge_OtpThrows_ReturnsServerError()
        {
            SetupMfaRequiredPolicy(mustEnroll: false, preferred: UserMfaType.Email);
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("boom"));
            _otpFactory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(otp.Object);

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.Password), new IdentityConfiguration(), new User { ItemId = "u1", Email = "e@x.com" });

            result.Error.Should().Be("server_error");
            result.ErrorDescription.Should().Be("Unable to initiate mfa challenge");
            result.StatusCode.Should().Be(500);
        }

        // ---------- ManageTokenAsync: tenant resolution ----------

        [Fact]
        public async Task ManageToken_ExemptGrant_TenantNotFound_ReturnsInvalidTenant()
        {
            // client_credentials is MFA-exempt so it proceeds straight to tenant resolution.
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.ClientCredential), new IdentityConfiguration(), new User { ItemId = "u1" });

            result.Error.Should().Be("invalid_tenant");
            result.ErrorDescription.Should().Be("Tenant not found");
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task ManageToken_PolicyNotRequired_TenantNotFound_ReturnsInvalidTenant()
        {
            _mfaPolicy.Setup(p => p.EvaluateAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaPolicyDecision { Required = false, AllowedMethods = new List<UserMfaType>() });
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.Password), new IdentityConfiguration(), new User { ItemId = "u1" });

            result.Error.Should().Be("invalid_tenant");
        }

        [Fact]
        public async Task ManageToken_Success_MintsAccessAndRefreshTokens()
        {
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(RefreshTokenAuthenticationServiceTests.MakeTenant());
            _jwtProvider.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken());

            var config = new IdentityConfiguration { AccessTokenValidForNumberMinutes = 15, RefreshTokenValidForNumberMinutes = 30 };
            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.ClientCredential), config, new User { ItemId = "u1" });

            result.StatusCode.Should().Be(200);
            result.Error.Should().BeNullOrEmpty();
            result.ExpiresIn.Should().Be(15 * 60);
            result.AccessToken.Should().NotBeNullOrWhiteSpace();
            result.RefreshToken.Should().NotBeNullOrWhiteSpace();
            new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken).Should().NotBeNull();
        }

        // ---------- ManageRefreshTokenAsync ----------

        [Fact]
        public async Task ManageRefreshToken_InitialFlow_ReturnsNonEmptyToken()
        {
            var tenant = RefreshTokenAuthenticationServiceTests.MakeTenant();
            var (token, expiry) = await Create().ManageRefreshTokenAsync(
                MakeRequest(GrantTypes.Password),
                RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken(),
                new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 30 },
                tenant,
                new User { ItemId = "u1" });

            token.Should().NotBeNullOrWhiteSpace();
            expiry.Should().BeAfter(DateTime.UtcNow);
            // Initial issue path persists the refresh token model and increments login info.
            _refreshRepo.Verify(r => r.CreateAsync(It.IsAny<Idp.DomainService.Oidc.Contracts.RefreshTokenModel>()), Times.Once);
            _userRepository.Verify(r => r.GetUserByIdAsync("u1"), Times.Once);
        }

        [Fact]
        public async Task ManageRefreshToken_RotationFlow_ReadsOldTokenFromCacheAndRevokes()
        {
            var oldCache = new Authentication.DomainService.Dtos.RefreshTokenCache
            {
                RefreshToken = "old-token",
                UserId = "u1",
                TenantId = "tenant-1",
                ClientId = "client-1",
                // Rotation inherits the lineage and its cap from the predecessor, so the cached entry
                // has to carry both for the rotation to be resolvable.
                RefreshTokenSessionId = "lineage-1",
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30),
                AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(7)
            };
            _cache.Setup(c => c.GetStringValueAsync("old-token"))
                .ReturnsAsync(System.Text.Json.JsonSerializer.Serialize(oldCache));
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            var request = MakeRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "old-token";

            var (token, _) = await Create().ManageRefreshTokenAsync(
                request,
                RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken(),
                new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 30 },
                RefreshTokenAuthenticationServiceTests.MakeTenant(),
                new User { ItemId = "u1" });

            token.Should().NotBeNullOrWhiteSpace();
            _cache.Verify(c => c.GetStringValueAsync("old-token"), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("old-token"), Times.Once);
            _refreshRepo.Verify(r => r.RevokeByTokenIdAsync("old-token", "superseded_by_rotation", It.IsAny<string>()), Times.Once);
        }
    }
}
