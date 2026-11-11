using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.Auth.OAuth;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 Q7, C3, C4, §6.3: <see cref="OAuthJwtAccessTokenManager.ManageTokenAsync"/> decides the organization
    /// first, so a refusal sends no MFA code, creates no IdP session and touches no refresh lineage.
    /// Also <see cref="TokenResponsePayload.Build"/> and the <see cref="OAuthError"/> constants (§6.2, §6.4).
    /// </summary>
    public sealed class OAuthJwtAccessTokenManagerOrganizationGateTests : IDisposable
    {
        private readonly Mock<IJwtAccessTokenProvider> _jwtProvider = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IMfaPolicyService> _mfaPolicy = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IOtpServiceFactory> _otpFactory = new();
        private readonly Mock<IOtpService> _otp = new();
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();
        private readonly Mock<IIdpSessionService> _idpSession = new();
        private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
        private readonly Mock<IUserRepository> _userRepository = new();

        public OAuthJwtAccessTokenManagerOrganizationGateTests()
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
            _refreshRepo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenModel>())).ReturnsAsync("id");
            _refreshRepo.Setup(r => r.TryClaimForRotationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            _userRepository.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(new User { ItemId = "u1" });

            // MFA would be required: a refusal must still send no code.
            _mfaPolicy.Setup(p => p.EvaluateAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaPolicyDecision
                {
                    Required = true,
                    MustEnrollFirst = false,
                    PreferredMethod = UserMfaType.Email,
                    AllowedMethods = new List<UserMfaType> { UserMfaType.Email }
                });
            _otp.Setup(o => o.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>())).ReturnsAsync(new OtpGenerationResponse { MfaId = "mfa-1" });
            _otpFactory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(_otp.Object);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(RefreshTokenAuthenticationServiceTests.MakeTenant());
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private OAuthJwtAccessTokenManager Create() =>
            new(_jwtProvider.Object, _authDomain.Object, _authRepo.Object, _mfaPolicy.Object, _cache.Object,
                _tenants.Object, _otpFactory.Object,
                new UnifiedTokenSessionService(_cache.Object, _authDomain.Object, _refreshRepo.Object, _dispatcher.Object, _idpSession.Object,
                    _httpContextAccessor.Object, _userRepository.Object, NullLogger<UnifiedTokenSessionService>.Instance),
                _idpSession.Object);

        private static TokenRequest MakeRequest(string grantType, string? refreshToken = null) => new()
        {
            GrantType = grantType,
            ClientId = "client-1",
            OrganizationId = "org-d",
            RefreshToken = refreshToken,
            Request = new DefaultHttpContext().Request
        };

        private void VerifyNothingIssued()
        {
            _mfaPolicy.Verify(p => p.EvaluateAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _otpFactory.Verify(f => f.GetOTPService(It.IsAny<UserMfaType>()), Times.Never);
            _otp.Verify(o => o.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>()), Times.Never);
            _idpSession.Verify(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _idpSession.Verify(s => s.IsSessionActiveAsync(It.IsAny<string>()), Times.Never);
            _jwtProvider.Verify(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()), Times.Never);
            _refreshRepo.Verify(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()), Times.Never);
            _refreshRepo.Verify(r => r.TryClaimForRotationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _cache.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
            _cache.Verify(c => c.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData(OrganizationIssuanceOutcome.OrganizationDisabled, 403, "organization_disabled")]
        [InlineData(OrganizationIssuanceOutcome.StatusUnavailable, 503, "temporarily_unavailable")]
        public async Task Q7_C3_C4_Refusal_ReturnedBeforeMfaIdpSessionAndRefresh(OrganizationIssuanceOutcome outcome, int status, string error)
        {
            _jwtProvider.Setup(p => p.ResolveOrganizationAsync(It.IsAny<User>(), It.IsAny<TokenRequest>()))
                .ReturnsAsync(JwtAccessTokenProvider.OrganizationRefusal(outcome));

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.Password), new IdentityConfiguration(), new User { ItemId = "u1", Email = "e@x.com" });

            result.StatusCode.Should().Be(status);
            result.Error.Should().Be(error);
            result.AccessToken.Should().BeNullOrEmpty();
            result.RefreshToken.Should().BeNullOrEmpty();
            result.MfaId.Should().BeNullOrEmpty();
            VerifyNothingIssued();
        }

        [Fact]
        public async Task C2_RefusedRefresh_NeitherRotatesNorRevokesTheLineage()
        {
            _jwtProvider.Setup(p => p.ResolveOrganizationAsync(It.IsAny<User>(), It.IsAny<TokenRequest>()))
                .ReturnsAsync(JwtAccessTokenProvider.OrganizationRefusal(OrganizationIssuanceOutcome.OrganizationDisabled));

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.RefreshToken, refreshToken: "old-token"), new IdentityConfiguration(), new User { ItemId = "u1" });

            result.StatusCode.Should().Be(403);
            _cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
            VerifyNothingIssued();
        }

        [Theory]
        [InlineData("disabled")]
        [InlineData(null)]
        public async Task H4_Success_CarriesTheDecisionsOrganizationStatus(string? status)
        {
            _jwtProvider.Setup(p => p.ResolveOrganizationAsync(It.IsAny<User>(), It.IsAny<TokenRequest>()))
                .Callback((User _, TokenRequest r) => r.OrganizationDecision = OrganizationScopeResolver.Issued(
                    OrganizationScopeResolver.ForStoredOrganization(status == null ? "org-a" : IdpConstants.NoOrganizationId), status))
                .ReturnsAsync((TokenResponse?)null);
            _jwtProvider.Setup(p => p.GetJwtAccessToken(It.IsAny<IdentityConfiguration>(), It.IsAny<Tenant>(), It.IsAny<User>(), It.IsAny<TokenRequest>(), It.IsAny<StateInfo>()))
                .ReturnsAsync(RefreshTokenAuthenticationServiceTests.MakeJwtAccessToken());

            var result = await Create().ManageTokenAsync(MakeRequest(GrantTypes.ClientCredential),
                new IdentityConfiguration { AccessTokenValidForNumberMinutes = 15, RefreshTokenValidForNumberMinutes = 30 }, new User { ItemId = "u1" });

            result.StatusCode.Should().Be(200);
            result.OrganizationStatus.Should().Be(status);
            _jwtProvider.Verify(p => p.ResolveOrganizationAsync(It.IsAny<User>(), It.IsAny<TokenRequest>()), Times.Once);
        }

        // ---------- TokenResponsePayload / OAuthError ----------

        [Fact]
        public void Payload_OrgStatusPresent_OnlyWhenSet()
        {
            TokenResponsePayload.Build(new TokenResponse { OrganizationStatus = "disabled" }, cookiesSet: true)
                .Should().ContainKey("org_status").WhoseValue.Should().Be("disabled");
            TokenResponsePayload.Build(new TokenResponse { OrganizationStatus = "disabled" }, cookiesSet: false)
                .Should().ContainKey("org_status");
            TokenResponsePayload.Build(new TokenResponse(), cookiesSet: false).Should().NotContainKey("org_status");
            TokenResponsePayload.Build(new TokenResponse { OrganizationStatus = " " }, cookiesSet: true).Should().NotContainKey("org_status");
        }

        [Fact]
        public void OAuthError_Constants_MatchTheSpec()
        {
            OAuthError.OrganizationDisabled.Should().Be("organization_disabled");
            OAuthError.OrganizationDisabledDescription.Should().Be("Your organization is disabled. Please contact your administrator or support.");
            OAuthError.TemporarilyUnavailable.Should().Be("temporarily_unavailable");
            OAuthError.OrganizationStatusUnavailableDescription.Should().Be("Organization status could not be checked. Please try again.");
        }
    }
}
