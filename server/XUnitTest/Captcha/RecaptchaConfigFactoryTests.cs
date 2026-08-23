using Blocks.CaptchaDriver;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Captcha
{
    public class RecaptchaConfigFactoryTests
    {
        private static IOptions<CaptchaOptions> Opts() => Options.Create(new CaptchaOptions());

        [Fact]
        public async Task GetRecaptchaConfig_UsesLocalConfig_WhenStoreIsEmpty()
        {
            var configService = new Mock<ICaptchaConfigurationService>();
            configService
                .Setup(s => s.GetCaptchaConfigurationAsync())
                .Returns(Task.FromResult<CaptchaConfiguration?>(null));
            var factory = new RecaptchaConfigFactory(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecaptchaConfigFactory>.Instance,
                configService.Object,
                UnusedResolver(),
                Opts());

            var result = await factory.GetRecaptchaConfig("https://x/{0}", "token-1");

            result.Should().BeOfType<LocalReCaptchaConfig>();
            result.ResolveRecaptchaUri().Should().Be("https://x/token-1");
        }

        [Fact]
        public async Task GetRecaptchaConfig_UsesDbConfig_WhenStoreHasConfig()
        {
            var configService = new Mock<ICaptchaConfigurationService>();
            configService
                .Setup(s => s.GetCaptchaConfigurationAsync())
                .Returns(Task.FromResult<CaptchaConfiguration?>(new CaptchaConfiguration { CaptchaSecret = "secret#1" }));
            var factory = new RecaptchaConfigFactory(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecaptchaConfigFactory>.Instance,
                configService.Object,
                UnusedResolver(),
                Opts());

            var result = await factory.GetRecaptchaConfig("https://x/{0}", "token-1");

            result.Should().BeOfType<DbReCaptchaConfig>();
            var uri = result.ResolveRecaptchaUri();
            uri.Should().Contain("secret%231");   // '#' URL-encoded
            uri.Should().EndWith("=token-1");
        }

        [Fact]
        public async Task GetRecaptchaConfig_FallsBackToLocal_OnException()
        {
            var configService = new Mock<ICaptchaConfigurationService>();
            configService
                .Setup(s => s.GetCaptchaConfigurationAsync())
                .Throws(new Exception("boom"));
            var factory = new RecaptchaConfigFactory(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecaptchaConfigFactory>.Instance,
                configService.Object,
                UnusedResolver(),
                Opts());

            var result = await factory.GetRecaptchaConfig("https://x/{0}", "token-1");
            result.Should().BeOfType<LocalReCaptchaConfig>();
        }
    
        /// <summary>
        /// These cases use legacy configurations (secret inline, no SecretId), so the vault
        /// resolver is never reached.
        /// </summary>
        private static ICaptchaSecretResolver UnusedResolver() => new Mock<ICaptchaSecretResolver>().Object;
}
}
