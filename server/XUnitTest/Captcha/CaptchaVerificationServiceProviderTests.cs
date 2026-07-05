using Blocks.CaptchaDriver;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace XUnitTest.Captcha
{
    public class CaptchaVerificationServiceProviderTests
    {
        private class StubService : ICaptchaVerificationService
        {
            public StubService(string provider) { Provider = provider; }
            public string Provider { get; }
            public Task<VerificationResult> VerifyAsync(string? verificationCode)
                => Task.FromResult(new VerificationResult { Verified = true, HostName = Provider });
        }

        [Fact]
        public void GetCaptchaVerificationService_ReturnsRegisteredProvider_CaseInsensitive()
        {
            var services = new ICaptchaVerificationService[]
            {
                new StubService("bcaptcha"),
                new StubService("recaptcha"),
                new StubService("hcaptcha")
            };
            var provider = new CaptchaVerificationServiceProvider(
                services, NullLogger<CaptchaVerificationServiceProvider>.Instance);

            provider.GetCaptchaVerificationService("ReCaptcha").Provider.Should().Be("recaptcha");
            provider.GetCaptchaVerificationService("HCAPTCHA").Provider.Should().Be("hcaptcha");
        }

        [Fact]
        public void GetCaptchaVerificationService_DefaultsToBcaptcha_WhenProviderIsEmpty()
        {
            var services = new ICaptchaVerificationService[] { new StubService("bcaptcha") };
            var provider = new CaptchaVerificationServiceProvider(
                services, NullLogger<CaptchaVerificationServiceProvider>.Instance);

            provider.GetCaptchaVerificationService("").Provider.Should().Be("bcaptcha");
        }

        [Fact]
        public void GetCaptchaVerificationService_Throws_WhenUnknown()
        {
            var services = new ICaptchaVerificationService[] { new StubService("bcaptcha") };
            var provider = new CaptchaVerificationServiceProvider(
                services, NullLogger<CaptchaVerificationServiceProvider>.Instance);

            Action act = () => provider.GetCaptchaVerificationService("foo");
            act.Should().Throw<InvalidOperationException>();
        }
    }
}
