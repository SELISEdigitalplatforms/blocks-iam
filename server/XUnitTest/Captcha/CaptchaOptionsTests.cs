using Blocks.CaptchaDriver;
using FluentAssertions;

namespace XUnitTest.Captcha
{
    public class CaptchaOptionsTests
    {
        [Fact]
        public void Defaults_AreApplied()
        {
            var options = new CaptchaOptions();

            options.VerificationCodeTtlSeconds.Should().Be(600);
            options.RecaptchaVerificationUrl.Should().Be("https://www.google.com/recaptcha/api/siteverify");
            options.HcaptchaVerificationUrl.Should().Be("https://api.hcaptcha.com/siteverify");
            options.BcaptchaVerificationUrl.Should().BeEmpty();
            options.ReCaptchaSecretKey.Should().BeNull();
        }

        [Fact]
        public void SectionName_IsCaptcha()
        {
            CaptchaOptions.SectionName.Should().Be("Captcha");
        }
    }
}
