using Blocks.Genesis;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using System.IdentityModel.Tokens.Jwt;

namespace XUnitTest.DomainService.OAuth
{
    public class OAuthJwtAccessTokenManagerTests
    {
        private readonly Mock<IJwtAccessTokenProvider> _jwtAccessTokenProvider;
        private readonly Mock<IAuthenticationDomainService> _authenticationDomainService;
        private readonly Mock<IOtpServiceFactory> _otpServiceFactory;
        private readonly Mock<IMfaConfigurationService> _configurationService;
        private readonly Mock<IMfaPolicyService> _mfaPolicyService;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<ITenants> _tenants;
        private readonly OAuthJwtAccessTokenManager _manager;

        public OAuthJwtAccessTokenManagerTests()
        {
            _jwtAccessTokenProvider = new Mock<IJwtAccessTokenProvider>();
            _authenticationDomainService = new Mock<IAuthenticationDomainService>();
            _otpServiceFactory = new Mock<IOtpServiceFactory>();
            _configurationService = new Mock<IMfaConfigurationService>();
            _mfaPolicyService = new Mock<IMfaPolicyService>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _tenants = new Mock<ITenants>();

            _mfaPolicyService.Setup(x => x.EvaluateAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaPolicyDecision { Required = false });

            var refreshTokenRepo = new Mock<IRefreshTokenRepository>();
            var unifiedTokenSession = new UnifiedTokenSessionService(_cacheClient.Object, _authenticationDomainService.Object, refreshTokenRepo.Object);

            _manager = new OAuthJwtAccessTokenManager(
                _jwtAccessTokenProvider.Object,
                _authenticationDomainService.Object,
                _authenticationRepository.Object,
                _configurationService.Object,
                _mfaPolicyService.Object,
                _cacheClient.Object,
                _tenants.Object,
                _otpServiceFactory.Object,
                unifiedTokenSession
            );
        }

        [Fact]
        public async Task ManageTokenAsync_WhenMfaPolicyRequires_ReturnsMfaResponse()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.Password);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: true);
            var otpService = new Mock<IOtpService>();

            _mfaPolicyService.Reset();
            _mfaPolicyService.Setup(x => x.EvaluateAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaPolicyDecision
                {
                    Required = true,
                    PreferredMethod = UserMfaType.Email,
                    AllowedMethods = new List<UserMfaType> { UserMfaType.Email }
                });
            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.Email)).Returns(otpService.Object);
            otpService.Setup(x => x.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>())).ReturnsAsync(new OtpGenerationResponse { MfaId = "mfa-123" });

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.Equal("mfa_enabled", result.Error);
            Assert.Equal("mfa-123", result.MfaId);
            Assert.Equal(UserMfaType.Email, result.UserMfa);
        }

        [Fact]
        public async Task ManageTokenAsync_WithoutMfa_ReturnsTokenResponse()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.Password);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: false);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType>() };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.NotNull(result.AccessToken);
            Assert.NotNull(result.RefreshToken);
            Assert.Equal(200, result.StatusCode);
        }

        [Fact]
        public async Task ManageTokenAsync_WithAuthCodeGrant_SetsCustomIssuer()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.AuthCode);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: false);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType>() };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _configuration.Setup(x => x["OpenIdConnect:IssuerUri"]).Returns("https://issuer.example.com");
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.Equal("https://issuer.example.com", jwtToken.Issuer);
        }

        [Fact]
        public async Task ManageTokenAsync_WithMfaCodeGrant_SkipsMfaCheck()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.MfaCode);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: true);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType> { UserMfaType.Email } };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.Null(result.Error);
            Assert.NotNull(result.AccessToken);
        }

        [Fact]
        public async Task ManageTokenAsync_WithRememberMe_ExtendsRefreshTokenLifetime()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.Password);
            tokenRequest.RememberMe = true;
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: false);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType>() };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 10080 * 60), Times.Once);
        }

        [Fact]
        public void CreateJwtAccessToken_CreatesValidToken()
        {
            var jwtToken = CreateJwtAccessToken();

            var token = OAuthJwtAccessTokenManager.CreateJwtAccessToken(jwtToken);

            Assert.NotNull(token);
            var handler = new JwtSecurityTokenHandler();
            Assert.True(handler.CanReadToken(token));
        }

        private TokenRequest CreateTokenRequest(string grantType)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.UserAgent = "Test Agent";
            return new TokenRequest
            {
                GrantType = grantType,
                Request = context.Request,
                Scope = "openid profile"
            };
        }

        private IdentityConfiguration CreateAuthConfig() => new()
        {
            AccessTokenValidForNumberMinutes = 15,
            RefreshTokenValidForNumberMinutes = 1440,
            RememberMeRefreshTokenValidForNumberMinutes = 10080,
            AccountLockDurationInMinutes = 30,
            GetNumberOfWrongAttemptsToLockTheAccount = 5
        };

        private User CreateUser(bool mfaEnabled) => new()
        {
            ItemId = "user-123",
            Email = "test@example.com",
            MfaEnabled = mfaEnabled,
            UserMfaType = UserMfaType.Email,
            Language = "en-US",
            OrganizationIds = new List<string> { "org-1" }
        };

        private Tenant CreateTenant() => new()
        {
            TenantId = "tenant-123",
            CookieDomain = ".example.com",
            ApplicationDomain = "app.example.com",
            DbConnectionString = "test-connection-string",
            JwtTokenParameters = new JwtTokenParameters()
            {
                PrivateCertificatePassword = "test-password",
                IssueDate = DateTime.UtcNow
            }
        };

        private JwtAccessToken CreateJwtAccessToken() => new()
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Claims = new List<System.Security.Claims.Claim>(),
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = null
        };
    }
}