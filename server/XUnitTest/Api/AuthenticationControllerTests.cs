using Api.Controllers;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Authentication.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="AuthenticationController"/>. All injected services are mocked; each
    /// test asserts the returned <see cref="IActionResult"/> type/status and verifies the delegated
    /// service call. Only branches contained in the controller actions are exercised.
    /// </summary>
    public class AuthenticationControllerTests : IDisposable
    {
        private const string ActorUserId = "actor-1";

        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IAccountService> _accountService = new();
        private readonly Mock<IAuthenticationFlowService> _flowService = new();
        private readonly Mock<IAuthenticationConfigurationService> _configService = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IAuthenticationDomainService> _domainService = new();

        public AuthenticationControllerTests()
        {
            BlocksContext.IsTestMode = true;
            SetContext(impersonated: false);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetContext(bool impersonated)
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: ActorUserId, impersonated: impersonated,
                isAuthenticated: true, requestUri: "https://test/auth", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        private AuthenticationController CreateController()
        {
            var controller = new AuthenticationController(
                _authService.Object,
                _accountService.Object,
                _flowService.Object,
                _configService.Object,
                _authRepo.Object,
                _domainService.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            return controller;
        }

        // ---------- ExecuteSignup ----------

        [Fact]
        public async Task ExecuteSignup_Success_ReturnsOk()
        {
            _accountService.Setup(s => s.SignupAccountAsync(It.IsAny<SignupUserRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = true });

            var result = await CreateController().ExecuteSignup(new SignupUserRequest());

            result.Should().BeOfType<OkObjectResult>();
            _accountService.Verify(s => s.SignupAccountAsync(It.IsAny<SignupUserRequest>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteSignup_Failure_ReturnsBadRequest()
        {
            _accountService.Setup(s => s.SignupAccountAsync(It.IsAny<SignupUserRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = false });

            var result = await CreateController().ExecuteSignup(new SignupUserRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- RetrieveLoginOptions ----------

        [Fact]
        public async Task RetrieveLoginOptions_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("options");
            _authService.Setup(s => s.GetLoginOptionsAsync()).ReturnsAsync(sentinel);

            var result = await CreateController().RetrieveLoginOptions();

            result.Should().BeSameAs(sentinel);
        }

        // ---------- ExecutePasswordLogin ----------

        [Fact]
        public async Task ExecutePasswordLogin_DelegatesFlowThenBuildsResult()
        {
            var flowResult = new AuthenticationFlowResult();
            var built = new OkObjectResult("logged-in");
            _flowService.Setup(f => f.ExecuteEmbeddedLoginAsync(It.IsAny<EmbeddedLoginRequest>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(flowResult);
            _authService.Setup(s => s.BuildFlowResultAsync(flowResult, It.IsAny<HttpContext>()))
                .ReturnsAsync(built);

            var result = await CreateController().ExecutePasswordLogin(new EmbeddedLoginRequest());

            result.Should().BeSameAs(built);
            _flowService.Verify(f => f.ExecuteEmbeddedLoginAsync(It.IsAny<EmbeddedLoginRequest>(), It.IsAny<HttpRequest>()), Times.Once);
        }

        // ---------- InitiateAccountRecovery ----------

        [Fact]
        public async Task InitiateAccountRecovery_Success_ReturnsOk()
        {
            _accountService.Setup(s => s.RecoverAccountAsync(It.IsAny<RecoveryUserRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = true });

            var result = await CreateController().InitiateAccountRecovery(new RecoveryUserRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task InitiateAccountRecovery_Failure_ReturnsBadRequest()
        {
            _accountService.Setup(s => s.RecoverAccountAsync(It.IsAny<RecoveryUserRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = false });

            var result = await CreateController().InitiateAccountRecovery(new RecoveryUserRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- ExecutePasswordReset ----------

        [Fact]
        public async Task ExecutePasswordReset_Success_ReturnsOk()
        {
            _accountService.Setup(s => s.ResetAccountPasswordAsync(It.IsAny<ResetPasswordRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = true });

            var result = await CreateController().ExecutePasswordReset(new ResetPasswordRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ExecutePasswordReset_Failure_ReturnsBadRequest()
        {
            _accountService.Setup(s => s.ResetAccountPasswordAsync(It.IsAny<ResetPasswordRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = false });

            var result = await CreateController().ExecutePasswordReset(new ResetPasswordRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- UpdatePassword ----------

        [Fact]
        public async Task UpdatePassword_Success_ReturnsOk()
        {
            _accountService.Setup(s => s.ChangePasswordAsync(It.IsAny<ChangePasswordRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = true });

            var result = await CreateController().UpdatePassword(new ChangePasswordRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task UpdatePassword_Failure_ReturnsBadRequest()
        {
            _accountService.Setup(s => s.ChangePasswordAsync(It.IsAny<ChangePasswordRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = false });

            var result = await CreateController().UpdatePassword(new ChangePasswordRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- Activate ----------

        [Fact]
        public async Task Activate_Success_ReturnsOk()
        {
            _accountService.Setup(s => s.ActivateAccountAsync(It.IsAny<ActivateUserRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = true });

            var result = await CreateController().Activate(new ActivateUserRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Activate_Failure_ReturnsBadRequest()
        {
            _accountService.Setup(s => s.ActivateAccountAsync(It.IsAny<ActivateUserRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = false });

            var result = await CreateController().Activate(new ActivateUserRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- ResendActivation ----------

        [Fact]
        public async Task ResendActivation_Success_ReturnsOk()
        {
            _accountService.Setup(s => s.ResendActivationAsync(It.IsAny<ResendActivationRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = true });

            var result = await CreateController().ResendActivation(new ResendActivationRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ResendActivation_Failure_ReturnsBadRequest()
        {
            _accountService.Setup(s => s.ResendActivationAsync(It.IsAny<ResendActivationRequest>()))
                .ReturnsAsync(new BaseAccountResponse { IsSuccess = false });

            var result = await CreateController().ResendActivation(new ResendActivationRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- ValidateActivationCode ----------

        [Fact]
        public async Task ValidateActivationCode_Success_ReturnsOk()
        {
            _accountService.Setup(s => s.ValidateAccountActivationCodeAsync(It.IsAny<ValidateActivationCodeRequest>()))
                .ReturnsAsync(new ActivationCodeValidationResponse { IsSuccess = true });

            var result = await CreateController().ValidateActivationCode(new ValidateActivationCodeRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ValidateActivationCode_Failure_ReturnsBadRequest()
        {
            _accountService.Setup(s => s.ValidateAccountActivationCodeAsync(It.IsAny<ValidateActivationCodeRequest>()))
                .ReturnsAsync(new ActivationCodeValidationResponse { IsSuccess = false });

            var result = await CreateController().ValidateActivationCode(new ValidateActivationCodeRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- InitiateSocialAuthentication ----------

        [Fact]
        public async Task InitiateSocialAuthentication_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("url");
            _authService.Setup(s => s.GetSocialAuthorizationUrlAsync("client", "https://cb"))
                .ReturnsAsync(sentinel);

            var result = await CreateController().InitiateSocialAuthentication("client", "https://cb");

            result.Should().BeSameAs(sentinel);
        }

        // ---------- HandleSocialCallback ----------

        [Fact]
        public async Task HandleSocialCallback_DelegatesFlowThenBuildsResult()
        {
            var flowResult = new AuthenticationFlowResult();
            var built = new OkObjectResult("cb");
            _flowService.Setup(f => f.ExecuteSocialLoginAsync(It.IsAny<SocialLoginRequest>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(flowResult);
            _authService.Setup(s => s.BuildFlowResultAsync(flowResult, It.IsAny<HttpContext>()))
                .ReturnsAsync(built);

            var result = await CreateController().HandleSocialCallback(new SocialLoginRequest());

            result.Should().BeSameAs(built);
        }

        // ---------- RefreshAccessToken ----------

        [Fact]
        public async Task RefreshAccessToken_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("refreshed");
            _flowService.Setup(f => f.ExecuteRefreshAsync(
                    It.IsAny<RefreshRequest>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                    It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().RefreshAccessToken(new RefreshRequest());

            result.Should().BeSameAs(sentinel);
        }

        // ---------- ExecuteLogout ----------

        [Fact]
        public async Task ExecuteLogout_NoRefreshToken_ReturnsBadRequest()
        {
            _authService.Setup(s => s.ExecuteLogoutAsync(It.IsAny<LogoutRequest>(), It.IsAny<HttpContext>()))
                .ReturnsAsync(new LogoutFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "invalid_request",
                    ErrorDescription = "Refresh token is required for logout"
                });

            var result = await CreateController().ExecuteLogout(new LogoutRequest { RefreshToken = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ExecuteLogout_ServiceFails_ReturnsBadRequest()
        {
            _authService.Setup(s => s.ExecuteLogoutAsync(It.IsAny<LogoutRequest>(), It.IsAny<HttpContext>()))
                .ReturnsAsync(new LogoutFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    LogoutResponse = new LogoutResponse { IsSuccess = false }
                });

            var result = await CreateController().ExecuteLogout(new LogoutRequest { RefreshToken = "rt" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ExecuteLogout_Success_ReturnsOk()
        {
            _authService.Setup(s => s.ExecuteLogoutAsync(It.IsAny<LogoutRequest>(), It.IsAny<HttpContext>()))
                .ReturnsAsync(new LogoutFlowResult
                {
                    StatusCode = StatusCodes.Status200OK,
                    LogoutResponse = new LogoutResponse { IsSuccess = true, IdpSessionId = "sess-1" }
                });

            var result = await CreateController().ExecuteLogout(new LogoutRequest { RefreshToken = "rt" });

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- SwitchOrganizationContext ----------

        [Fact]
        public async Task SwitchOrganizationContext_DelegatesFlowThenBuildsResult()
        {
            var flowResult = new AuthenticationFlowResult();
            var built = new OkObjectResult("switched");
            _flowService.Setup(f => f.ExecuteSwitchOrganizationAsync(
                    It.IsAny<SwitchOrganizationRequest>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(flowResult);
            _authService.Setup(s => s.BuildFlowResultAsync(flowResult, It.IsAny<HttpContext>()))
                .ReturnsAsync(built);

            var result = await CreateController().SwitchOrganizationContext(new SwitchOrganizationRequest());

            result.Should().BeSameAs(built);
        }

        // ---------- InitiateImpersonation ----------

        [Fact]
        public async Task InitiateImpersonation_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("impersonated");
            _flowService.Setup(f => f.ExecuteImpersonateAsync(
                    It.IsAny<ImpersonateRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().InitiateImpersonation(new ImpersonateRequest());

            result.Should().BeSameAs(sentinel);
        }

        // ---------- StopImpersonation ----------

        [Fact]
        public async Task StopImpersonation_NotImpersonating_ReturnsBadRequest()
        {
            // Default context has Impersonated = false.
            var result = await CreateController().StopImpersonation(new StopImpersonationRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
            _flowService.Verify(f => f.ExecuteStopImpersonationAsync(
                It.IsAny<StopImpersonationRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()), Times.Never);
        }

        [Fact]
        public async Task StopImpersonation_Impersonating_DelegatesAndReturnsResult()
        {
            SetContext(impersonated: true);
            var sentinel = new OkObjectResult("stopped");
            _flowService.Setup(f => f.ExecuteStopImpersonationAsync(
                    It.IsAny<StopImpersonationRequest>(), It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
                .ReturnsAsync(sentinel);

            var result = await CreateController().StopImpersonation(new StopImpersonationRequest());

            result.Should().BeSameAs(sentinel);
        }

        // ---------- GetImpersonationStatus ----------

        [Fact]
        public async Task GetImpersonationStatus_ReturnsOk()
        {
            var result = await CreateController().GetImpersonationStatus();

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- ExecuteGlobalLogout ----------

        [Fact]
        public async Task ExecuteGlobalLogout_ServiceFails_ReturnsBadRequest()
        {
            _authService.Setup(s => s.LogoutAll(It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = false });

            var result = await CreateController().ExecuteGlobalLogout(new LogoutAllRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ExecuteGlobalLogout_Success_NoBackchannel_ReturnsOk()
        {
            _authService.Setup(s => s.LogoutAll(It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = true, IdpSessionIds = new[] { "sess-1" } });
            _authService.Setup(s => s.UpdateIdpSessionForLogoutAsync(It.IsAny<HttpContext>(),
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(), true, It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(false);

            var result = await CreateController().ExecuteGlobalLogout(new LogoutAllRequest { UseBackchannel = false });

            result.Should().BeOfType<OkObjectResult>();
            _authService.Verify(s => s.TriggerBackchannelLogoutAllAsync(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteGlobalLogout_NullRequest_UsesDefaultAndReturnsOk()
        {
            _authService.Setup(s => s.LogoutAll(It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = true });

            var result = await CreateController().ExecuteGlobalLogout(null);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ExecuteGlobalLogout_Backchannel_TriggersBackchannel()
        {
            _authService.Setup(s => s.LogoutAll(It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = true });
            _authService.Setup(s => s.TriggerBackchannelLogoutAllAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(true);

            var result = await CreateController().ExecuteGlobalLogout(new LogoutAllRequest { UseBackchannel = true });

            result.Should().BeOfType<OkObjectResult>();
            _authService.Verify(s => s.TriggerBackchannelLogoutAllAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        // ---------- RetrieveUserInformation ----------

        [Fact]
        public async Task RetrieveUserInformation_InvalidToken_ReturnsUnauthorized()
        {
            _authService.Setup(s => s.BuildOidcUserInfoAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .ReturnsAsync((false, new Dictionary<string, object>()));

            var result = await CreateController().RetrieveUserInformation();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task RetrieveUserInformation_Valid_ReturnsOk()
        {
            _authService.Setup(s => s.BuildOidcUserInfoAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
                .ReturnsAsync((true, new Dictionary<string, object> { { "sub", "u-1" } }));

            var result = await CreateController().RetrieveUserInformation();

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- CreateIdentityProvider ----------

        [Fact]
        public async Task CreateIdentityProvider_NullRequest_ReturnsBadRequest()
        {
            var result = await CreateController().CreateIdentityProvider(null);

            result.Should().BeOfType<BadRequestObjectResult>();
            _authService.Verify(s => s.CreateIdentityProviderAsync(It.IsAny<SaveIdentityProviderRequest>()), Times.Never);
        }

        [Fact]
        public async Task CreateIdentityProvider_Success_ReturnsOk()
        {
            _authService.Setup(s => s.CreateIdentityProviderAsync(It.IsAny<SaveIdentityProviderRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateController().CreateIdentityProvider(new SaveIdentityProviderRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task CreateIdentityProvider_Failure_ReturnsBadRequest()
        {
            _authService.Setup(s => s.CreateIdentityProviderAsync(It.IsAny<SaveIdentityProviderRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = false });

            var result = await CreateController().CreateIdentityProvider(new SaveIdentityProviderRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- GetAllIdentityProviders ----------

        [Fact]
        public async Task GetAllIdentityProviders_ReturnsOk()
        {
            _authService.Setup(s => s.GetAllIdentityProvidersAsync())
                .ReturnsAsync(new List<Authentication.DomainService.Entities.IdentityProvider>());

            var result = await CreateController().GetAllIdentityProviders();

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- GetIdentityProviderById ----------

        [Fact]
        public async Task GetIdentityProviderById_NotFound_ReturnsNotFound()
        {
            _authService.Setup(s => s.GetIdentityProviderByIdAsync("missing"))
                .ReturnsAsync((Authentication.DomainService.Entities.IdentityProvider)null);

            var result = await CreateController().GetIdentityProviderById("missing");

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task GetIdentityProviderById_Found_ReturnsOk()
        {
            _authService.Setup(s => s.GetIdentityProviderByIdAsync("id-1"))
                .ReturnsAsync(new Authentication.DomainService.Entities.IdentityProvider
                {
                    Provider = "google",
                    ProviderType = "social",
                    ClientId = "cid",
                    ClientSecret = "secret",
                    TokenEndpointAuthMethod = "client_secret_post"
                });

            var result = await CreateController().GetIdentityProviderById("id-1");

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- UpdateIdentityProvider ----------

        [Fact]
        public async Task UpdateIdentityProvider_NullRequest_ReturnsBadRequest()
        {
            var result = await CreateController().UpdateIdentityProvider("id-1", null);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task UpdateIdentityProvider_Success_ReturnsOk()
        {
            _authService.Setup(s => s.UpdateIdentityProviderAsync("id-1", It.IsAny<UpdateIdentityProviderRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateController().UpdateIdentityProvider("id-1", new UpdateIdentityProviderRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task UpdateIdentityProvider_Failure_ReturnsBadRequest()
        {
            _authService.Setup(s => s.UpdateIdentityProviderAsync("id-1", It.IsAny<UpdateIdentityProviderRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = false });

            var result = await CreateController().UpdateIdentityProvider("id-1", new UpdateIdentityProviderRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- DeleteIdentityProvider ----------

        [Fact]
        public async Task DeleteIdentityProvider_Success_ReturnsOk()
        {
            _authService.Setup(s => s.DeleteIdentityProviderAsync("id-1"))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateController().DeleteIdentityProvider("id-1");

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task DeleteIdentityProvider_Failure_ReturnsBadRequest()
        {
            _authService.Setup(s => s.DeleteIdentityProviderAsync("id-1"))
                .ReturnsAsync(new BaseResponse { IsSuccess = false });

            var result = await CreateController().DeleteIdentityProvider("id-1");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- UpdateIdentityProviderStatus ----------

        [Fact]
        public async Task UpdateIdentityProviderStatus_Success_ReturnsOk()
        {
            _authService.Setup(s => s.UpdateIdentityProviderStatusAsync("id-1", true))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateController().UpdateIdentityProviderStatus("id-1", new UpdateStatusRequest { IsActive = true });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task UpdateIdentityProviderStatus_Failure_ReturnsBadRequest()
        {
            _authService.Setup(s => s.UpdateIdentityProviderStatusAsync("id-1", false))
                .ReturnsAsync(new BaseResponse { IsSuccess = false });

            var result = await CreateController().UpdateIdentityProviderStatus("id-1", new UpdateStatusRequest { IsActive = false });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- Get / Update config ----------

        [Fact]
        public async Task Get_Config_DelegatesAndReturnsResult()
        {
            var sentinel = new OkObjectResult("config");
            _configService.Setup(c => c.GetAuthenticationConfigAsync()).ReturnsAsync(sentinel);

            var result = await CreateController().GetAuthenticationConfiguration();

            result.Should().BeSameAs(sentinel);
        }

        [Fact]
        public async Task Update_Config_DelegatesAndReturnsResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _configService.Setup(c => c.UpdateAuthenticationConfigAsync(It.IsAny<UpdateAuthenticationConfigurationRequest>()))
                .ReturnsAsync(response);

            var result = await CreateController().UpdateAuthenticationConfiguration(new UpdateAuthenticationConfigurationRequest());

            result.Should().BeSameAs(response);
        }

        // ---------- User codes ----------

        [Fact]
        public async Task GenerateUserCode_DelegatesAndReturnsResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _domainService.Setup(d => d.GenerateUserCodeByClientAsync(It.IsAny<GenerateUserCodeRequest>()))
                .ReturnsAsync(response);

            var result = await CreateController().GenerateUserCode(new GenerateUserCodeRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetUserCodes_DelegatesToRepository()
        {
            var list = new List<GetUserCodesByUserIdResponse>();
            _authRepo.Setup(r => r.GetUserCodesByUserIdAsync(ActorUserId)).ReturnsAsync(list);

            var result = await CreateController().GetUserCodes();

            result.Should().BeSameAs(list);
            _authRepo.Verify(r => r.GetUserCodesByUserIdAsync(ActorUserId), Times.Once);
        }

        // ---------- Client credentials ----------

        [Fact]
        public async Task SaveClientCredential_DelegatesAndReturnsResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _domainService.Setup(d => d.SaveClientCredentialAsync(It.IsAny<SaveClientCredentialRequest>()))
                .ReturnsAsync(response);

            var result = await CreateController().SaveClientCredential(new SaveClientCredentialRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task DeleteClientCredential_DelegatesWithItemId()
        {
            var response = new BaseResponse { IsSuccess = true };
            _domainService.Setup(d => d.DeleteClientCredentialAsync(It.Is<DeleteClientCredentialRequest>(r => r.ItemId == "cc-1")))
                .ReturnsAsync(response);

            var result = await CreateController().DeleteClientCredential("cc-1");

            result.Should().BeSameAs(response);
            _domainService.Verify(d => d.DeleteClientCredentialAsync(It.Is<DeleteClientCredentialRequest>(r => r.ItemId == "cc-1")), Times.Once);
        }

        [Fact]
        public async Task GetClientCredentials_DelegatesToRepository()
        {
            var list = new List<ClientCredential>();
            _authRepo.Setup(r => r.GetClientCredentialsAsync()).ReturnsAsync(list);

            var result = await CreateController().GetClientCredentials();

            result.Should().BeSameAs(list);
        }
    }
}
