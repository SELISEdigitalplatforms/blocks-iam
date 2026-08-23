using Blocks.CaptchaDriver;
using FluentAssertions;

namespace XUnitTest.Captcha
{
    public class DbReCaptchaConfigTests
    {
        [Fact]
        public void ResolveRecaptchaUri_EncodesSpecialCharsInSecret()
        {
            var target = new DbReCaptchaConfig("secret with space & #1", "token");

            var uri = target.ResolveRecaptchaUri();

            uri.Should().StartWith("https://www.google.com/recaptcha/api/siteverify?secret=");
            uri.Should().Contain("secret%20with%20space%20%26%20%231");
            uri.Should().EndWith("=token");
        }

        [Fact]
        public void ResolveRecaptchaUri_TokenPlaceholderSubstituted()
        {
            var target = new DbReCaptchaConfig("k", "tok#2");

            var uri = target.ResolveRecaptchaUri();

            uri.Should().Be("https://www.google.com/recaptcha/api/siteverify?secret=k&response=tok#2");
        }

        [Fact]
        public void ResolveRecaptchaUri_AcceptsNullToken()
        {
            var target = new DbReCaptchaConfig("k", null);

            var uri = target.ResolveRecaptchaUri();

            uri.Should().Be("https://www.google.com/recaptcha/api/siteverify?secret=k&response=");
        }
    }
}
