using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;
using System.Text.Json;
using Iam.DomainService.Resources;

namespace XUnitTest.Auth
{
    public class OidcLoginOrchestratorTests : IDisposable
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IMfaChallengeIssuer> _mfaIssuer = new();
        private readonly Mock<Authentication.DomainService.Authentication.IAuthenticationService> _authService = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<ICaptchaEvaluator> _captcha = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        private readonly PasswordHasher _passwordHasher;
        private readonly OidcLoginAuditWriter _auditWriter;
        private readonly OidcCaptchaEvaluator _captchaEvaluator;
        private readonly OidcAuthorizationEndpoint _authorizationEndpoint;

        private const string TenantId = "tenant-1";
        private const string Plaintext = "Secret123!";

        public OidcLoginOrchestratorTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));

            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _authDomain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);
            _mfaIssuer.Setup(m => m.WriteAuditAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            _passwordHasher = new PasswordHasher(NullLogger<PasswordHasher>.Instance);
            _auditWriter = new OidcLoginAuditWriter(_authDomain.Object, _activity.Object, NullLogger<OidcLoginAuditWriter>.Instance);
            _captchaEvaluator = new OidcCaptchaEvaluator(_captcha.Object);
            _authorizationEndpoint = new OidcAuthorizationEndpoint(
                new Mock<IAuthorizationCodeRepository>().Object,
                new Mock<IIdpSessionRepository>().Object,
                new Mock<IPkceService>().Object,
                _userRepo.Object,
                _repo.Object,
                _authService.Object,
                _tenants.Object,
                _cache.Object,
                new Mock<IResourceRepository>().Object,
                NullLogger<OidcAuthorizationEndpoint>.Instance);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private OidcLoginOrchestrator Create() =>
            new(_repo.Object, _mfaIssuer.Object, _authService.Object, _tenants.Object, _cache.Object,
                _userRepo.Object, _passwordHasher, _auditWriter, _captchaEvaluator, _authorizationEndpoint,
                NullLogger<OidcLoginOrchestrator>.Instance);

        private static object? Prop(object? value, string name) =>
            value?.GetType().GetProperty(name)?.GetValue(value);

        private static HttpRequest Req() => new DefaultHttpContext().Request;
        private static HttpResponse Res() => new DefaultHttpContext().Response;

        private static User ValidUser() => new()
        {
            ItemId = "user-1",
            Email = "u@example.com",
            Active = true,
            IsVerified = true,
            Password = BCrypt.Net.BCrypt.HashPassword(Plaintext),
            UserMfaType = UserMfaType.Email,
            FailedLoginCount = 0
        };

        private OidcLoginRequest PasswordRequest(string password = Plaintext, string? scope = null) => new()
        {
            Username = "u@example.com",
            Password = password,
            ClientId = "client-1",
            RedirectUri = "https://app.example.com/callback",
            TenantId = TenantId,
            Scope = scope
        };

        private void SetupOidcClientExists() =>
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync(new OidcClientRegistration { ClientId = "client-1" });

        // ---------- routing: social ----------

        [Fact]
        public async Task ExecuteAsync_InitiatesSocialLogin_WhenProviderClientIdSet()
        {
            var expected = new OkObjectResult(new { url = "https://social" });
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            _authService.Setup(s => s.GetOidcSocialAuthorizationUrlAsync("prov-1", It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(expected);

            var request = new OidcLoginRequest { ProviderClientId = "prov-1", ProviderRedirectUri = "https://cb" };
            var result = await Create().ExecuteAsync(request, Req(), Res());

            result.Should().BeSameAs(expected);
            _cache.Verify(c => c.AddStringValueAsync(It.Is<string>(k => k.StartsWith("oidc_context:")), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
            _authService.Verify(s => s.GetOidcSocialAuthorizationUrlAsync("prov-1", It.IsAny<string>(), "https://cb"), Times.Once);
        }

        // ---------- password: validation ----------

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidRequest_WhenUsernameOrPasswordMissing()
        {
            var result = await Create().ExecuteAsync(new OidcLoginRequest { Username = "", Password = "" }, Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidClient_WhenClientIdMissing()
        {
            var request = new OidcLoginRequest { Username = "u", Password = "p", ClientId = "" };
            var result = await Create().ExecuteAsync(request, Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidRequest_WhenRedirectUriMissing()
        {
            var request = new OidcLoginRequest { Username = "u", Password = "p", ClientId = "client-1", RedirectUri = "" };
            var result = await Create().ExecuteAsync(request, Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidClient_WhenOidcClientNotConfigured()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ReturnsAsync((OidcClientRegistration)null!);

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_client");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsUnauthorized_WhenUserNull()
        {
            SetupOidcClientExists();
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((User)null!);

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            Prop(unauth.Value, "error").Should().Be("invalid_credentials");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsUnauthorized_WhenUserInactive()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            user.Active = false;
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsLocked_WhenUserLockedOut()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        // ---------- password: captcha ----------

        [Fact]
        public async Task ExecuteAsync_RequiresCaptcha_AndWritesAudit_WhenCaptchaMissing()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            user.FailedLoginCount = 2; // triggers captcha gate
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _captcha.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key" });

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "captcha_required").Should().Be(true);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.AtLeastOnce);
        }

        // ---------- password: wrong password ----------

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidCredentials_WhenPasswordWrong_NotLocked()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "user-1", LockoutUntilUtc = null });

            var result = await Create().ExecuteAsync(PasswordRequest(password: "wrong-password"), Req(), Res());

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            Prop(unauth.Value, "error").Should().Be("invalid_credentials");
            _repo.Verify(r => r.IncrementFailedLoginAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsLocked_WhenPasswordWrong_TriggersLockout()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "user-1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) });

            var result = await Create().ExecuteAsync(PasswordRequest(password: "wrong-password"), Req(), Res());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        // ---------- password: MFA challenge ----------

        [Fact]
        public async Task ExecuteAsync_Returns500_WhenMfaRequired_ButOtpServiceNull()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _mfaIssuer.Setup(m => m.IsRequiredAsync(It.IsAny<User>())).ReturnsAsync(true);
            _mfaIssuer.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync((IOtpService)null!);

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        }

        [Fact]
        public async Task ExecuteAsync_Returns500_WhenMfaGenerateFails()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _mfaIssuer.Setup(m => m.IsRequiredAsync(It.IsAny<User>())).ReturnsAsync(true);
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.GenerateAsync(It.IsAny<global::Mfa.DomainService.Entities.UserInfo>(), It.IsAny<string>()))
                .ReturnsAsync(new OtpGenerationResponse { IsSuccess = false });
            _mfaIssuer.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync(otp.Object);

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsMfaChallenge_WhenMfaRequired()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _mfaIssuer.Setup(m => m.IsRequiredAsync(It.IsAny<User>())).ReturnsAsync(true);
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.GenerateAsync(It.IsAny<global::Mfa.DomainService.Entities.UserInfo>(), It.IsAny<string>()))
                .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true, MfaId = "mfa-1" });
            _mfaIssuer.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync(otp.Object);
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Create().ExecuteAsync(PasswordRequest(), Req(), Res());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            Prop(ok.Value, "error").Should().Be("mfa_enabled");
            Prop(ok.Value, "mfa_id").Should().Be("mfa-1");
            _cache.Verify(c => c.AddStringValueAsync("oidc_mfa_login:mfa-1", It.IsAny<string>(), It.IsAny<long>()), Times.Once);
        }

        // ---------- password: happy path reaches authorize endpoint ----------

        [Fact]
        public async Task ExecuteAsync_ReachesAuthorizeEndpoint_AndWritesSuccessAudit_OnValidLogin()
        {
            SetupOidcClientExists();
            var user = ValidUser();
            user.FailedMfaCount = 1; // forces ResetAuthFailureCountersAsync to persist
            _repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(user);
            _mfaIssuer.Setup(m => m.IsRequiredAsync(It.IsAny<User>())).ReturnsAsync(false);
            _repo.Setup(r => r.UpdatePartialAsync<User>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // scope without "openid" makes the authorize endpoint validation fail deterministically
            var result = await Create().ExecuteAsync(PasswordRequest(scope: "profile email"), Req(), Res());

            result.Should().BeOfType<BadRequestObjectResult>();
            _repo.Verify(r => r.UpdatePartialAsync<User>("user-1", It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Outcome == "success")), Times.AtLeastOnce);
        }

        // ---------- CompleteMfaLoginAsync ----------

        private static string MfaContextJson(string userId = "user-1", string scope = "profile") =>
            JsonSerializer.Serialize(new
            {
                UserId = userId,
                ClientId = "client-1",
                RedirectUri = "https://app.example.com/callback",
                Scope = scope,
                State = "st",
                Nonce = "no",
                CodeChallenge = "",
                CodeChallengeMethod = "S256",
                TenantId = TenantId
            });

        private OidcLoginRequest MfaRequest(string mfaId = "mfa-1", string? mfaCode = "123456") => new()
        {
            MfaId = mfaId,
            MfaCode = mfaCode,
            ClientId = "client-1"
        };

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidRequest_WhenMfaCodeMissing()
        {
            var result = await Create().ExecuteAsync(MfaRequest(mfaCode: null), Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_request");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidMfaSession_WhenContextMissing()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_mfa_session");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidMfaSession_WhenContextHasNoUserId()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(MfaContextJson(userId: ""));

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            Prop(bad.Value, "error").Should().Be("invalid_mfa_session");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsUnauthorized_WhenMfaUserNull()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(MfaContextJson());
            _repo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((User)null!);

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsLocked_WhenMfaUserLocked()
        {
            var user = ValidUser();
            user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(MfaContextJson());
            _repo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        [Fact]
        public async Task ExecuteAsync_Returns500_WhenMfaCompleteOtpServiceNull()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(MfaContextJson());
            _repo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            _mfaIssuer.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync((IOtpService)null!);

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsInvalidMfaCode_WhenVerifyFails_NotLocked()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(MfaContextJson());
            _repo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.IncrementFailedMfaAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "user-1", LockoutUntilUtc = null });
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(new OtpVerificationResponse { IsValid = false });
            _mfaIssuer.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync(otp.Object);

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            var unauth = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            Prop(unauth.Value, "error").Should().Be("invalid_mfa_code");
            _mfaIssuer.Verify(m => m.WriteAuditAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsLocked_WhenVerifyFails_TriggersLockout()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(MfaContextJson());
            _repo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
            _repo.Setup(r => r.IncrementFailedMfaAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new User { ItemId = "user-1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(20) });
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(new OtpVerificationResponse { IsValid = false });
            _mfaIssuer.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync(otp.Object);

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            var obj = result.Should().BeOfType<ObjectResult>().Subject;
            obj.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        [Fact]
        public async Task ExecuteAsync_CompletesMfa_ReachesAuthorize_AndClearsSession_OnValidCode()
        {
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync(MfaContextJson(scope: "profile"));
            _repo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(ValidUser());
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            var otp = new Mock<IOtpService>();
            otp.Setup(o => o.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(new OtpVerificationResponse { IsValid = true });
            _mfaIssuer.Setup(m => m.GetOtpServiceAsync(It.IsAny<User>())).ReturnsAsync(otp.Object);

            var result = await Create().ExecuteAsync(MfaRequest(), Req(), Res());

            // authorize endpoint rejects the deliberately-invalid scope, but the full happy path executed
            result.Should().BeOfType<BadRequestObjectResult>();
            _cache.Verify(c => c.RemoveKeyAsync("oidc_mfa_login:mfa-1"), Times.Once);
            _mfaIssuer.Verify(m => m.WriteAuditAsync(It.Is<MfaAuditEvent>(e => e.Status == "success"), It.IsAny<CancellationToken>()), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Outcome == "success")), Times.AtLeastOnce);
        }
    }
}
