using Authentication.DomainService.Authentication;
using FluentAssertions;

namespace XUnitTest.Auth
{
    public class CaptchaGateTests
    {
        [Fact]
        public void IsCaptchaRequired_ReturnsFalse_WhenUserIsNull()
        {
            CaptchaGate.IsCaptchaRequired(null).Should().BeFalse();
        }

        [Fact]
        public void IsCaptchaRequired_ReturnsFalse_WhenFailedCountBelowThreshold()
        {
            var user = new Iam.DomainService.Entities.User { FailedLoginCount = 0 };
            CaptchaGate.IsCaptchaRequired(user).Should().BeFalse();

            user.FailedLoginCount = 1;
            CaptchaGate.IsCaptchaRequired(user).Should().BeFalse();
        }

        [Fact]
        public void IsCaptchaRequired_ReturnsTrue_AtThreshold()
        {
            var user = new Iam.DomainService.Entities.User { FailedLoginCount = CaptchaGate.FailedAttemptsBeforeCaptcha };
            CaptchaGate.IsCaptchaRequired(user).Should().BeTrue();
        }

        [Fact]
        public void IsCaptchaRequired_ReturnsTrue_AboveThreshold()
        {
            var user = new Iam.DomainService.Entities.User { FailedLoginCount = 10 };
            CaptchaGate.IsCaptchaRequired(user).Should().BeTrue();
        }

        [Fact]
        public void FailedAttemptsBeforeCaptcha_IsTwo()
        {
            CaptchaGate.FailedAttemptsBeforeCaptcha.Should().Be(2);
        }
    }
}