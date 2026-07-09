using Blocks.CaptchaDriver;
using FluentAssertions;

namespace XUnitTest.Captcha
{
    public class LocalReCaptchaConfigTests
    {
        [Fact]
        public void ResolveRecaptchaUri_SubstitutesToken()
        {
            var target = new LocalReCaptchaConfig("https://x/{0}", "token-1");
            target.ResolveRecaptchaUri().Should().Be("https://x/token-1");
        }

        [Fact]
        public void Constructor_ThrowsOnNullTemplate()
        {
            Action act = () => new LocalReCaptchaConfig(null!, "x");
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_AcceptsNullToken()
        {
            var target = new LocalReCaptchaConfig("https://x/{0}", null);
            target.ResolveRecaptchaUri().Should().Be("https://x/");
        }
    }
}
