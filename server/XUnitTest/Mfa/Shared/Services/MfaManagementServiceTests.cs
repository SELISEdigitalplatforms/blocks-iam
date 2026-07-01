using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;

namespace XUnitTest.Mfa.Shared.Services
{
    public class MfaManagementServiceTests
    {
        private static MfaManagementService CreateService(
            out Mock<IOtpServiceFactory> factory,
            out Mock<IMfaManagementRepository> repo,
            out Mock<IMfaConfigurationService> config,
            out Mock<ICacheClient> cache,
            out Mock<IMfaAuditService> audit)
        {
            factory = new Mock<IOtpServiceFactory>();
            repo = new Mock<IMfaManagementRepository>();
            config = new Mock<IMfaConfigurationService>();
            cache = new Mock<ICacheClient>();
            audit = new Mock<IMfaAuditService>();

            return new MfaManagementService(
                factory.Object,
                repo.Object,
                config.Object,
                cache.Object,
                audit.Object);
        }

        [Fact]
        public async Task GenerateOTPAsync_WhenMfaDisabled_ReturnsErrorDict()
        {
            var service = CreateService(out _, out _, out var config, out _, out _);
            config.Setup(c => c.GetAsync()).ReturnsAsync(new Mfa.DomainService.Configuration.Configuration { EnableMfa = false });

            var result = await service.GenerateOTPAsync(new OtpGenerationRequest { UserId = "u1" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("mfa_not_enable");
        }

        [Fact]
        public async Task GenerateOTPAsync_WhenUserIdEmpty_ReturnsErrorDict()
        {
            var service = CreateService(out _, out _, out var config, out _, out _);
            config.Setup(c => c.GetAsync()).ReturnsAsync(new Mfa.DomainService.Configuration.Configuration { EnableMfa = true });

            var result = await service.GenerateOTPAsync(new OtpGenerationRequest { UserId = "" });

            result.Errors.Should().ContainKey("empty_user_id");
        }

        [Fact]
        public async Task GenerateOTPAsync_WhenUserInfoNull_ReturnsErrorDict()
        {
            var service = CreateService(out _, out var repo, out var config, out _, out _);
            config.Setup(c => c.GetAsync()).ReturnsAsync(new Mfa.DomainService.Configuration.Configuration { EnableMfa = true });
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserInfo?)null);

            var result = await service.GenerateOTPAsync(new OtpGenerationRequest { UserId = "u1" });

            result.Errors.Should().ContainKey("user_not_found");
        }

        [Fact]
        public async Task GenerateOTPAsync_DelegatesToFactoryAndService()
        {
            var service = CreateService(out var factory, out var repo, out var config, out _, out _);
            config.Setup(c => c.GetAsync()).ReturnsAsync(new Mfa.DomainService.Configuration.Configuration { EnableMfa = true });
            var userInfo = new UserInfo { ItemId = "u1", Email = "a@b.c", UserMfaType = UserMfaType.TOTP };
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(userInfo);
            var otpService = new Mock<IOtpService>();
            var expected = new OtpGenerationResponse { IsSuccess = true, MfaId = "m1" };
            otpService.Setup(s => s.GenerateAsync(userInfo, "")).ReturnsAsync(expected);
            factory.Setup(f => f.GetOTPService(UserMfaType.TOTP)).Returns(otpService.Object);

            var result = await service.GenerateOTPAsync(new OtpGenerationRequest { UserId = "u1" });

            result.Should().BeSameAs(expected);
            factory.Verify(f => f.GetOTPService(UserMfaType.TOTP), Times.Once);
            otpService.Verify(s => s.GenerateAsync(userInfo, ""), Times.Once);
        }

        [Fact]
        public async Task GenerateOTPAsync_PassesPhoneAsEmailDomain()
        {
            var service = CreateService(out var factory, out var repo, out var config, out _, out _);
            config.Setup(c => c.GetAsync()).ReturnsAsync(new Mfa.DomainService.Configuration.Configuration { EnableMfa = true });
            var userInfo = new UserInfo { ItemId = "u1", Email = "a@b.c", UserMfaType = UserMfaType.Email };
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(userInfo);
            var otpService = new Mock<IOtpService>();
            otpService.Setup(s => s.GenerateAsync(userInfo, "sms.example.com")).ReturnsAsync(new OtpGenerationResponse { IsSuccess = true });
            factory.Setup(f => f.GetOTPService(UserMfaType.Email)).Returns(otpService.Object);

            var result = await service.GenerateOTPAsync(new OtpGenerationRequest { UserId = "u1", MfaType = UserMfaType.Email, SendPhoneNumberAsEmailDomain = "sms.example.com" });

            result.IsSuccess.Should().BeTrue();
            otpService.Verify(s => s.GenerateAsync(userInfo, "sms.example.com"), Times.Once);
        }

        [Fact]
        public async Task VerifyOTPAsync_WhenInvalid_DoesNotUpdateRepository_ButWritesFailureAudit()
        {
            var service = CreateService(out var factory, out var repo, out _, out _, out var audit);
            var otpService = new Mock<IOtpService>();
            var verification = new OtpVerificationResponse { IsValid = false, Errors = new Dictionary<string, string> { { "code", "bad" } } };
            otpService.Setup(s => s.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(verification);
            factory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(otpService.Object);

            var result = await service.VerifyOTPAsync(new VerifyOtpRequest { AuthType = UserMfaType.Email, MfaId = "mfa-1" });

            result.Should().BeSameAs(verification);
            repo.Verify(r => r.UpdatePartialAsync<UserMfaInfo>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
            audit.Verify(a => a.WriteAsync(It.Is<MfaAuditEvent>(e => e.EventType == "mfa_verification_failure" && e.Status == "failure"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task VerifyOTPAsync_WhenValid_AndNotFromToken_UpdatesUserMfaInfoAndWritesSuccessAudit()
        {
            var service = CreateService(out var factory, out var repo, out _, out _, out var audit);
            var otpService = new Mock<IOtpService>();
            var verification = new OtpVerificationResponse { IsValid = true, UserId = "u1" };
            otpService.Setup(s => s.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(verification);
            factory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(otpService.Object);
            Dictionary<string, object>? captured = null;
            repo.Setup(r => r.UpdatePartialAsync<UserMfaInfo>("u1", It.IsAny<Dictionary<string, object>>(), "Users"))
                .Callback<string, Dictionary<string, object>, string>((_, u, _) => captured = u)
                .Returns(Task.CompletedTask);

            var result = await service.VerifyOTPAsync(new VerifyOtpRequest { AuthType = UserMfaType.TOTP, IsFromTokenCall = false });

            result.Should().BeSameAs(verification);
            captured.Should().NotBeNull();
            captured!.Should().ContainKey("MfaEnabled");
            captured["MfaEnabled"].Should().Be(true);
            audit.Verify(a => a.WriteAsync(It.Is<MfaAuditEvent>(e => e.EventType == "mfa_verification_success" && e.Status == "success"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task VerifyOTPAsync_WhenValid_AndFromToken_SkipsPartialUpdate_ButWritesAudit()
        {
            var service = CreateService(out var factory, out var repo, out _, out _, out var audit);
            var otpService = new Mock<IOtpService>();
            var verification = new OtpVerificationResponse { IsValid = true, UserId = "u1" };
            otpService.Setup(s => s.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(verification);
            factory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(otpService.Object);

            await service.VerifyOTPAsync(new VerifyOtpRequest { AuthType = UserMfaType.TOTP, IsFromTokenCall = true });

            repo.Verify(r => r.UpdatePartialAsync<UserMfaInfo>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
            audit.Verify(a => a.WriteAsync(It.Is<MfaAuditEvent>(e => e.EventType == "mfa_verification_success"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task VerifyOTPAsync_WhenAuditServiceNull_StillSucceeds()
        {
            var factory = new Mock<IOtpServiceFactory>();
            var repo = new Mock<IMfaManagementRepository>();
            var config = new Mock<IMfaConfigurationService>();
            var cache = new Mock<ICacheClient>();
            var service = new MfaManagementService(factory.Object, repo.Object, config.Object, cache.Object, null);

            var otpService = new Mock<IOtpService>();
            var verification = new OtpVerificationResponse { IsValid = true, UserId = "u1" };
            otpService.Setup(s => s.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(verification);
            factory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(otpService.Object);

            var result = await service.VerifyOTPAsync(new VerifyOtpRequest { AuthType = UserMfaType.Email });

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task DisableUserMfa_WhenUserIdEmpty_ReturnsError()
        {
            var service = CreateService(out _, out _, out _, out _, out _);
            var result = await service.DisableUserMfa(new DisableUserMfaRequest { UserId = "" });
            result.Errors.Should().ContainKey("empty_user_id");
        }

        [Fact]
        public async Task DisableUserMfa_WhenNonAdmin_AndNotSelf_ReturnsError()
        {
            var service = CreateService(out _, out _, out _, out _, out _);
            var result = await service.DisableUserMfa(new DisableUserMfaRequest { UserId = "u1" });
            result.Errors.Should().ContainKey("invalid_user_id");
        }

        [Fact]
        public async Task DisableUserMfa_NonAdmin_AndNotSelf_DoesNotWriteAudit()
        {
            var service = CreateService(out _, out var repo, out _, out _, out var audit);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "target", UserMfaType = UserMfaType.Email });
            repo.Setup(r => r.UpdatePartialAsync<UserMfaInfo>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var result = await service.DisableUserMfa(new DisableUserMfaRequest { UserId = "target" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_user_id");
            repo.Verify(r => r.UpdatePartialAsync<UserMfaInfo>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
            audit.Verify(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DisableUserMfa_AdminReset_WritesMfaResetAuditWithActorAndReason()
        {
            var service = CreateService(out _, out var repo, out _, out _, out var audit);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "target", UserMfaType = UserMfaType.Email });
            repo.Setup(r => r.UpdatePartialAsync<UserMfaInfo>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            MfaAuditEvent? captured = null;
            audit.Setup(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>()))
                .Callback<MfaAuditEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            var result = await service.DisableUserMfa(new DisableUserMfaRequest
            {
                UserId = "target",
                AdminActorUserId = "admin-1",
                Reason = "lost device"
            });

            result.IsSuccess.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.EventType.Should().Be("mfa_reset");
            captured.Details.Should().Contain("actor=admin-1");
            captured.Details.Should().Contain("reason=lost device");
        }

        [Fact]
        public async Task DisableUserMfa_AdminReset_WithoutReason_StillWritesAudit()
        {
            var service = CreateService(out _, out var repo, out _, out _, out var audit);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserInfo?)null);
            repo.Setup(r => r.UpdatePartialAsync<UserMfaInfo>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            MfaAuditEvent? captured = null;
            audit.Setup(a => a.WriteAsync(It.IsAny<MfaAuditEvent>(), It.IsAny<CancellationToken>()))
                .Callback<MfaAuditEvent, CancellationToken>((e, _) => captured = e)
                .Returns(Task.CompletedTask);

            await service.DisableUserMfa(new DisableUserMfaRequest { UserId = "target", AdminActorUserId = "admin-1" });

            captured.Should().NotBeNull();
            captured!.Details.Should().Contain("reason=");
        }

        [Fact]
        public async Task ResendOtpAsync_WhenKeyMissing_ReturnsError()
        {
            var service = CreateService(out _, out _, out _, out var cache, out _);
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            var result = await service.ResendOtpAsync("missing-mfa", "");

            result.Errors.Should().ContainKey("message");
        }

        [Fact]
        public async Task ResendOtpAsync_WhenKeyPresent_DelegatesToGenerate()
        {
            var service = CreateService(out var factory, out var repo, out var config, out var cache, out _);
            var context = MfaAuthenticationContext.Create("mfa-1", "user-1");
            cache.Setup(c => c.KeyExistsAsync("mfa-1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("mfa-1")).ReturnsAsync(context.Sterilize());
            config.Setup(c => c.GetAsync()).ReturnsAsync(new global::Mfa.DomainService.Configuration.Configuration { EnableMfa = true });
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "user-1", UserMfaType = UserMfaType.Email });
            var otpService = new Mock<IOtpService>();
            otpService.Setup(s => s.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>()))
                .ReturnsAsync(new OtpGenerationResponse { IsSuccess = true });
            factory.Setup(f => f.GetOTPService(UserMfaType.Email)).Returns(otpService.Object);

            var result = await service.ResendOtpAsync("mfa-1", "");

            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
        }
    }
}
