using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using FluentAssertions;
using Iam.DomainService.Entities;

namespace XUnitTest.DomainService.Authentication
{
    public class CaptchaGateTests
    {
        [Fact]
        public void IsCaptchaRequired_ReturnsFalse_WhenUserIsNull()
        {
            CaptchaGate.IsCaptchaRequired(null).Should().BeFalse();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void IsCaptchaRequired_ReturnsFalse_BeforeThreshold(int failedLoginCount)
        {
            var user = new User { ItemId = "u1", FailedLoginCount = failedLoginCount };
            CaptchaGate.IsCaptchaRequired(user).Should().BeFalse();
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(10)]
        public void IsCaptchaRequired_ReturnsTrue_AtOrAboveThreshold(int failedLoginCount)
        {
            var user = new User { ItemId = "u1", FailedLoginCount = failedLoginCount };
            CaptchaGate.IsCaptchaRequired(user).Should().BeTrue();
        }

        [Fact]
        public void FailedAttemptsBeforeCaptcha_IsTwo()
        {
            CaptchaGate.FailedAttemptsBeforeCaptcha.Should().Be(2);
        }

        [Fact]
        public void OAuthError_ExposesCaptchaInvalidConstant()
        {
            OAuthError.CaptchaInvalid.Should().Be("captcha_invalid");
            OAuthError.CaptchaEnabled.Should().Be("captcha_enabled");
        }

        [Fact]
        public void LoginAuditEvents_ExposesExpectedEventNames()
        {
            LoginAuditEvents.LoginSuccess.Should().Be("login_success");
            LoginAuditEvents.LoginFailure.Should().Be("login_failure");
            LoginAuditEvents.LoginFailureAccountLocked.Should().Be("login_failure_account_locked");
            LoginAuditEvents.CaptchaValidationSuccess.Should().Be("captcha_validation_success");
            LoginAuditEvents.CaptchaValidationFailure.Should().Be("captcha_validation_failure");
        }
    }
}
