using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaProcessorTests
    {
        private static CaptchaProcessor CreateProcessor(
            out Mock<ICacheClient> cache,
            out Mock<ICaptchaVerificationServiceProvider> provider)
        {
            cache = new Mock<ICacheClient>();
            provider = new Mock<ICaptchaVerificationServiceProvider>();
            var options = Options.Create(new CaptchaOptions { VerificationCodeTtlSeconds = 60 });
            return new CaptchaProcessor(cache.Object, provider.Object, options);
        }

        [Fact]
        public async Task SubmitAndCreateVerificationCodeAsync_WritesHostNameWithTtl()
        {
            var processor = CreateProcessor(out var cache, out _);
            string? capturedKey = null;
            string? capturedValue = null;
            TimeSpan? capturedTtl = null;

            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((k, v, t) =>
                {
                    capturedKey = k;
                    capturedValue = v;
                    capturedTtl = TimeSpan.FromSeconds(t);
                })
                .ReturnsAsync(true);
            cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            var code = await processor.SubmitAndCreateVerificationCodeAsync("cap-id-1", "site.example.com");

            code.Should().NotBeNullOrEmpty();
            capturedKey.Should().StartWith("captcha:vc:");
            capturedValue.Should().Be("site.example.com");
            capturedTtl.Should().Be(TimeSpan.FromSeconds(60));
            cache.Verify(c => c.RemoveKeyAsync("cap-id-1"), Times.Once);
        }

        [Fact]
        public async Task VerifyCaptchaAsync_ForwardsVerificationCodeToVerificationService()
        {
            var processor = CreateProcessor(out _, out var provider);
            var verification = new Mock<ICaptchaVerificationService>();
            verification.SetupGet(v => v.Provider).Returns("bcaptcha");
            verification.Setup(v => v.VerifyAsync("CODE"))
                .ReturnsAsync(new VerificationResult { Verified = true, HostName = "h" });
            provider.Setup(p => p.GetCaptchaVerificationService("bcaptcha")).Returns(verification.Object);

            var result = await processor.VerifyCaptchaAsync("bcaptcha", "CODE");

            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("h");
        }

        [Fact]
        public async Task VerifyCaptchaAsync_ThrowsOnNullCode()
        {
            var processor = CreateProcessor(out _, out _);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(
                () => processor.VerifyCaptchaAsync("bcaptcha", null!));
        }
    }
}
