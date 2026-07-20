using Api.Controllers;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Moq;
using StorageDriver;
using MfaConfiguration = Mfa.DomainService.Configuration.Configuration;
using UserActivityEvent = Iam.DomainService.Dtos.UserActivityEvent;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="MfaController"/>. Services are mocked; every action asserts the
    /// returned <see cref="IActionResult"/> type/status and verifies the service interactions.
    /// <see cref="TotpService"/> is a concrete (non-virtual) type the controller depends on, so it is
    /// constructed with mocked dependencies that return defaults; only the reachable controller
    /// branches around it are exercised.
    /// </summary>
    public class MfaControllerTests : IDisposable
    {
        private const string ActorUserId = "actor-1";

        private readonly Mock<IMfaManagementService> _mfaManagement = new();
        private readonly Mock<IMfaConfigurationService> _mfaConfig = new();
        private readonly Mock<IMfaBackupCodeService> _backupCodes = new();
        private readonly Mock<IMfaAuditService> _audit = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();
        private readonly Mock<IMfaManagementRepository> _totpRepo = new();

        public MfaControllerTests()
        {
            BlocksContext.IsTestMode = true;
            SetAuthenticatedContext();
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetAuthenticatedContext()
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: ActorUserId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test/mfa", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        private TotpService BuildTotpService()
        {
            var config = new ConfigurationBuilder().Build();
            return new TotpService(
                _totpRepo.Object,
                NullLogger<TotpService>.Instance,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                config,
                new Mock<ICacheClient>().Object,
                new Mock<IValidator<VerifyOtpRequest>>().Object,
                new Mock<ITenants>().Object,
                new Mock<IStorageDriverService>().Object);
        }

        private MfaController CreateController()
        {
            var controller = new MfaController(
                _mfaManagement.Object,
                _mfaConfig.Object,
                _backupCodes.Object,
                _audit.Object,
                _authRepo.Object,
                BuildTotpService(),
                _dispatcher.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            return controller;
        }

        // ---------- GetPolicy ----------

        [Fact]
        public async Task GetPolicy_NoStoredConfig_ReturnsOkWithDefaults()
        {
            _mfaConfig.Setup(c => c.GetAsync()).ReturnsAsync((MfaConfiguration?)null);

            var result = await CreateController().GetMfaConfig();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetPolicy_WithStoredConfig_ReturnsOk()
        {
            _mfaConfig.Setup(c => c.GetAsync()).ReturnsAsync(new MfaConfiguration
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.Email },
                RequireMfaForAllUsers = true,
                AllowBackupCodes = true,
                BackupCodesCount = 10
            });

            var result = await CreateController().GetMfaConfig();

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- UpdatePolicy ----------

        [Fact]
        public async Task UpdatePolicy_AppliesFields_SavesAndAudits()
        {
            _mfaConfig.Setup(c => c.GetAsync()).ReturnsAsync(new MfaConfiguration { UserMfaType = new List<UserMfaType>() });
            _mfaConfig.Setup(c => c.SaveAsync(It.IsAny<MfaConfiguration>())).Returns(Task.CompletedTask);

            var request = new UpdateMfaPolicyRequest
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP },
                RequireMfaForAllUsers = true,
                MfaRequiredRoles = new List<string> { "admin" },
                MfaExemptRoles = new List<string> { "svc" },
                AllowUserOptOut = false,
                AllowBackupCodes = true,
                BackupCodesCount = 8
            };

            var result = await CreateController().UpdateMfaConfig(request, CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var saved = ok.Value.Should().BeOfType<MfaConfiguration>().Subject;
            saved.EnableMfa.Should().BeTrue();
            saved.RequireMfaForAllUsers.Should().BeTrue();
            saved.BackupCodesCount.Should().Be(8);
            _mfaConfig.Verify(c => c.SaveAsync(It.IsAny<MfaConfiguration>()), Times.Once);
            _audit.Verify(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- SetupTotp ----------

        [Fact]
        public async Task SetupTotp_NoUser_ReturnsUnauthorized()
        {
            BlocksContext.SetContext(null);

            var result = await CreateController().SetupTotp();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task SetupTotp_UserNotFoundInTotpService_ReturnsBadRequest()
        {
            // _totpRepo.GetItemAsync<UserInfo> returns null by default -> "user_not_exist" error dict.
            var result = await CreateController().SetupTotp();

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- VerifyTotpSetup ----------

        [Fact]
        public async Task VerifyTotpSetup_MissingCode_ReturnsBadRequest()
        {
            var result = await CreateController().VerifyTotpSetup(new VerifyTotpSetupRequest { Code = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task VerifyTotpSetup_NoUser_ReturnsBadRequest()
        {
            BlocksContext.SetContext(null);

            var result = await CreateController().VerifyTotpSetup(new VerifyTotpSetupRequest { Code = "123456" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task VerifyTotpSetup_InvalidCode_DispatchesFailureAndReturnsBadRequest()
        {
            // TOTP not set up (repo returns null) -> verification.IsValid == false.
            var result = await CreateController().VerifyTotpSetup(new VerifyTotpSetupRequest { Code = "000000" });

            result.Should().BeOfType<BadRequestObjectResult>();
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
            _authRepo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- GenerateOtp ----------

        [Fact]
        public async Task GenerateOtp_NoUser_ReturnsUnauthorized()
        {
            BlocksContext.SetContext(null);

            var result = await CreateController().GenerateOtp(new GenerateOtpRequest());

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task GenerateOtp_ServiceReturnsErrors_ReturnsBadRequest()
        {
            _mfaManagement.Setup(m => m.GenerateOTPAsync(It.IsAny<OtpGenerationRequest>()))
                .ReturnsAsync(new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "e", "bad" } } });

            var result = await CreateController().GenerateOtp(new GenerateOtpRequest { MfaType = UserMfaType.Email });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GenerateOtp_Success_ReturnsOkWithMfaId()
        {
            _mfaManagement.Setup(m => m.GenerateOTPAsync(It.IsAny<OtpGenerationRequest>()))
                .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true, MfaId = "mfa-123" });

            var result = await CreateController().GenerateOtp(null);

            result.Should().BeOfType<OkObjectResult>();
            _mfaManagement.Verify(m => m.GenerateOTPAsync(It.Is<OtpGenerationRequest>(r => r.UserId == ActorUserId)), Times.Once);
        }

        // ---------- ResendOtp ----------

        [Fact]
        public async Task ResendOtp_MissingMfaId_ReturnsBadRequest()
        {
            var result = await CreateController().ResendOtp(new ResendOtpApiRequest { MfaId = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ResendOtp_ServiceReturnsErrors_ReturnsBadRequest()
        {
            _mfaManagement.Setup(m => m.ResendOtpAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "e", "x" } } });

            var result = await CreateController().ResendOtp(new ResendOtpApiRequest { MfaId = "mfa-1" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ResendOtp_Success_ReturnsOk()
        {
            _mfaManagement.Setup(m => m.ResendOtpAsync("mfa-1", It.IsAny<string>()))
                .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true, MfaId = "mfa-1" });

            var result = await CreateController().ResendOtp(new ResendOtpApiRequest { MfaId = "mfa-1", SendPhoneNumberAsEmailDomain = "d" });

            result.Should().BeOfType<OkObjectResult>();
            _mfaManagement.Verify(m => m.ResendOtpAsync("mfa-1", "d"), Times.Once);
        }

        // ---------- VerifyOtp ----------

        [Fact]
        public async Task VerifyOtp_MissingFields_ReturnsBadRequest()
        {
            var result = await CreateController().VerifyOtp(new VerifyOtpApiRequest { MfaId = "mfa-1", VerificationCode = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task VerifyOtp_InvalidCode_ReturnsBadRequest()
        {
            _mfaManagement.Setup(m => m.VerifyOTPAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = false, Errors = new Dictionary<string, string> { { "e", "x" } } });

            var result = await CreateController().VerifyOtp(new VerifyOtpApiRequest { MfaId = "mfa-1", VerificationCode = "123456" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task VerifyOtp_Valid_ReturnsOk()
        {
            _mfaManagement.Setup(m => m.VerifyOTPAsync(It.IsAny<VerifyOtpRequest>()))
                .ReturnsAsync(new OtpVerificationResponse { IsValid = true, UserId = "u-9" });

            var result = await CreateController().VerifyOtp(new VerifyOtpApiRequest { MfaId = "mfa-1", VerificationCode = "123456" });

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- SetMfaMethod ----------

        [Fact]
        public async Task SetMfaMethod_NullRequest_ReturnsBadRequest()
        {
            var result = await CreateController().SetMfaMethod(null);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SetMfaMethod_UserNotFound_ReturnsNotFound()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId)).ReturnsAsync((User)null);

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.Email });

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task SetMfaMethod_Email_NotVerified_ReturnsBadRequest()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, EmailVerifiedAtUtc = null });

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.Email });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SetMfaMethod_Email_Verified_EnablesAndDispatches()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, EmailVerifiedAtUtc = DateTime.UtcNow, UserMfaType = UserMfaType.None });

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.Email });

            result.Should().BeOfType<OkObjectResult>();
            _authRepo.Verify(r => r.UpdatePartialAsync<User>(ActorUserId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Once);
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task SetMfaMethod_Email_AlreadyEmail_DoesNotDispatch()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, EmailVerifiedAtUtc = DateTime.UtcNow, UserMfaType = UserMfaType.Email });

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.Email });

            result.Should().BeOfType<OkObjectResult>();
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Never);
        }

        [Fact]
        public async Task SetMfaMethod_Totp_NotEnrolled_ReturnsBadRequest()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, MfaEnabled = false });

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.TOTP });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SetMfaMethod_Totp_Enrolled_SetsPreferredAndAudits()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, MfaEnabled = true, UserMfaType = UserMfaType.Email });

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.TOTP });

            result.Should().BeOfType<OkObjectResult>();
            _authRepo.Verify(r => r.UpdatePartialAsync<User>(ActorUserId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Once);
            _audit.Verify(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetMfaMethod_None_DisablesAndDispatches()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, UserMfaType = UserMfaType.Email });
            _mfaManagement.Setup(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.None });

            result.Should().BeOfType<OkObjectResult>();
            _mfaManagement.Verify(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()), Times.Once);
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task SetMfaMethod_None_DisableReturnsErrors_ReturnsBadRequest()
        {
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, UserMfaType = UserMfaType.Email });
            _mfaManagement.Setup(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()))
                .ReturnsAsync(new BaseResponse { Errors = new Dictionary<string, string> { { "e", "x" } } });

            var result = await CreateController().SetMfaMethod(new SetMfaMethodRequest { MfaType = UserMfaType.None });

            result.Should().BeOfType<BadRequestObjectResult>();
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Never);
        }

        // ---------- DisableMfa ----------

        [Fact]
        public async Task DisableMfa_NoUser_ReturnsUnauthorized()
        {
            BlocksContext.SetContext(null);

            var result = await CreateController().DisableMfa();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task DisableMfa_ServiceErrors_ReturnsBadRequest()
        {
            _mfaManagement.Setup(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()))
                .ReturnsAsync(new BaseResponse { Errors = new Dictionary<string, string> { { "e", "x" } } });

            var result = await CreateController().DisableMfa();

            result.Should().BeOfType<BadRequestObjectResult>();
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Never);
        }

        [Fact]
        public async Task DisableMfa_Success_ReturnsOkAndDispatches()
        {
            _mfaManagement.Setup(m => m.DisableUserMfa(It.IsAny<DisableUserMfaRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateController().DisableMfa();

            result.Should().BeOfType<OkObjectResult>();
            _dispatcher.Verify(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        // ---------- GetBackupCodesStatus ----------

        [Fact]
        public async Task GetBackupCodesStatus_NoUser_ReturnsUnauthorized()
        {
            BlocksContext.SetContext(null);

            var result = await CreateController().GetBackupCodesStatus();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task GetBackupCodesStatus_ReturnsOkWithRemaining()
        {
            _backupCodes.Setup(b => b.GetRemainingCountAsync(ActorUserId, It.IsAny<CancellationToken>())).ReturnsAsync(4);

            var result = await CreateController().GetBackupCodesStatus();

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- GenerateBackupCodes ----------

        [Fact]
        public async Task GenerateBackupCodes_NoUser_ReturnsUnauthorized()
        {
            BlocksContext.SetContext(null);

            var result = await CreateController().GenerateBackupCodes();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task GenerateBackupCodes_BackupDisabled_ReturnsBadRequest()
        {
            _mfaConfig.Setup(c => c.GetAsync()).ReturnsAsync(new MfaConfiguration { AllowBackupCodes = false });

            var result = await CreateController().GenerateBackupCodes();

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GenerateBackupCodes_NotEnrolled_ReturnsBadRequest()
        {
            _mfaConfig.Setup(c => c.GetAsync()).ReturnsAsync(new MfaConfiguration { AllowBackupCodes = true, BackupCodesCount = 5 });
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, MfaEnabled = false });

            var result = await CreateController().GenerateBackupCodes();

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GenerateBackupCodes_GenerateFails_ReturnsBadRequest()
        {
            _mfaConfig.Setup(c => c.GetAsync()).ReturnsAsync(new MfaConfiguration { AllowBackupCodes = true, BackupCodesCount = 5 });
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, MfaEnabled = true });
            _backupCodes.Setup(b => b.GenerateAsync(ActorUserId, 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaBackupCodeGenerationResult { IsSuccess = false, Errors = new Dictionary<string, string> { { "e", "x" } } });

            var result = await CreateController().GenerateBackupCodes();

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GenerateBackupCodes_Success_ReturnsOkWithCodes()
        {
            _mfaConfig.Setup(c => c.GetAsync()).ReturnsAsync(new MfaConfiguration { AllowBackupCodes = true, BackupCodesCount = 3 });
            _authRepo.Setup(r => r.GetUserByIdAsync(ActorUserId))
                .ReturnsAsync(new User { ItemId = ActorUserId, MfaEnabled = true });
            _backupCodes.Setup(b => b.GenerateAsync(ActorUserId, 3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaBackupCodeGenerationResult { IsSuccess = true, PlainCodes = new List<string> { "a", "b", "c" } });

            var result = await CreateController().GenerateBackupCodes();

            result.Should().BeOfType<OkObjectResult>();
            _backupCodes.Verify(b => b.GenerateAsync(ActorUserId, 3, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- ConsumeBackupCode ----------

        [Fact]
        public async Task ConsumeBackupCode_MissingFields_ReturnsBadRequest()
        {
            var result = await CreateController().ConsumeBackupCode(new ConsumeBackupCodeRequest { UserId = "", Code = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ConsumeBackupCode_Invalid_ReturnsBadRequest()
        {
            _backupCodes.Setup(b => b.ConsumeAsync("u-1", "code-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaBackupCodeConsumeResult { IsValid = false, Errors = new Dictionary<string, string> { { "e", "x" } } });

            var result = await CreateController().ConsumeBackupCode(new ConsumeBackupCodeRequest { UserId = "u-1", Code = "code-1" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ConsumeBackupCode_Valid_ReturnsOk()
        {
            _backupCodes.Setup(b => b.ConsumeAsync("u-1", "code-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MfaBackupCodeConsumeResult { IsValid = true, UserId = "u-1" });

            var result = await CreateController().ConsumeBackupCode(new ConsumeBackupCodeRequest { UserId = "u-1", Code = "code-1" });

            result.Should().BeOfType<OkObjectResult>();
            _backupCodes.Verify(b => b.ConsumeAsync("u-1", "code-1", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
