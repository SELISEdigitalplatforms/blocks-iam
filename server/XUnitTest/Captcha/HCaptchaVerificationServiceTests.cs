using System.Net;
using Blocks.CaptchaDriver;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Captcha
{
    public class HCaptchaVerificationServiceTests
    {
        private static HCaptchaVerificationService CreateService(
            Mock<ICaptchaConfigurationService> configService,
            Mock<IHttpClientService> httpClient,
            string verificationUrl = "https://api.hcaptcha.com/siteverify",
            CaptchaConfiguration? config = null,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string responseBody = "{\"success\":true,\"hostname\":\"site.example.com\"}")
        {
            configService
                .Setup(c => c.GetCaptchaConfigurationAsync())
                .Returns(Task.FromResult<CaptchaConfiguration?>(config));

            httpClient
                .Setup(h => h.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string?>()))
                .ReturnsAsync(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseBody)
                });

            var options = Options.Create(new CaptchaOptions { HcaptchaVerificationUrl = verificationUrl });

            return new HCaptchaVerificationService(
                configService.Object,
                options,
                NullLogger<HCaptchaVerificationService>.Instance,
                httpClient.Object);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsVerified_OnSuccess()
        {
            var config = new CaptchaConfiguration { CaptchaSecret = "secret" };
            var service = CreateService(new Mock<ICaptchaConfigurationService>(), new Mock<IHttpClientService>(), config: config);

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("site.example.com");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_WhenConfigMissing()
        {
            var service = CreateService(new Mock<ICaptchaConfigurationService>(), new Mock<IHttpClientService>(), config: null);

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_WhenSecretMissing()
        {
            var config = new CaptchaConfiguration { CaptchaSecret = string.Empty };
            var service = CreateService(new Mock<ICaptchaConfigurationService>(), new Mock<IHttpClientService>(), config: config);

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_OnHttpError()
        {
            var config = new CaptchaConfiguration { CaptchaSecret = "secret" };
            var service = CreateService(new Mock<ICaptchaConfigurationService>(), new Mock<IHttpClientService>(),
                config: config, statusCode: HttpStatusCode.InternalServerError);

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_OnHttpException()
        {
            var configService = new Mock<ICaptchaConfigurationService>();
            configService.Setup(c => c.GetCaptchaConfigurationAsync())
                .Returns(Task.FromResult<CaptchaConfiguration?>(new CaptchaConfiguration { CaptchaSecret = "s" }));
            var httpClient = new Mock<IHttpClientService>();
            httpClient.Setup(h => h.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string?>()))
                .ThrowsAsync(new HttpRequestException("boom"));

            var options = Options.Create(new CaptchaOptions());
            var service = new HCaptchaVerificationService(
                configService.Object, options,
                NullLogger<HCaptchaVerificationService>.Instance, httpClient.Object);

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_WhenSuccessFalse()
        {
            var config = new CaptchaConfiguration { CaptchaSecret = "secret" };
            var service = CreateService(new Mock<ICaptchaConfigurationService>(), new Mock<IHttpClientService>(),
                config: config, responseBody: "{\"success\":false,\"hostname\":null}");

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
        }

        [Fact]
        public void Provider_IsHcaptcha()
        {
            var service = CreateService(new Mock<ICaptchaConfigurationService>(), new Mock<IHttpClientService>());
            service.Provider.Should().Be("hcaptcha");
        }
    }
}
