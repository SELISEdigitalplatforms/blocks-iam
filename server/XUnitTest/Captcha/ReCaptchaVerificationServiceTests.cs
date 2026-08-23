using System.Net;
using System.Text.Json;
using Blocks.CaptchaDriver;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Captcha
{
    public class ReCaptchaVerificationServiceTests
    {
        private static ReCaptchaVerificationService CreateService(
            Mock<IHttpClientService> httpClient,
            string? token = "captcha-token",
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string responseBody = "{\"success\":true,\"hostname\":\"site.example.com\"}",
            IRecaptchaConfig? config = null,
            Exception? thrownByHttp = null)
        {
            httpClient
                .Setup(h => h.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string?>()))
                .ReturnsAsync(() =>
                {
                    if (thrownByHttp is not null) throw thrownByHttp;
                    return new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(responseBody)
                    };
                });

            var configFactory = new Mock<IRecaptchaConfigFactory>();
            var effectiveConfig = config ?? new StubRecaptchaConfig(
                "https://www.google.com/recaptcha/api/siteverify?secret=k&response=" + "{0}");
            configFactory
                .Setup(f => f.GetRecaptchaConfig(It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(effectiveConfig);

            var options = Options.Create(new CaptchaOptions
            {
                RecaptchaVerificationUrl = "https://www.google.com/recaptcha/api/siteverify"
            });

            return new ReCaptchaVerificationService(
                httpClient.Object,
                options,
                NullLogger<ReCaptchaVerificationService>.Instance,
                configFactory.Object);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsVerified_WhenResponseSuccess()
        {
            var service = CreateService(new Mock<IHttpClientService>());

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("site.example.com");
            result.Errors.Should().BeNull();
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_WhenResponseSuccessFalse()
        {
            var service = CreateService(new Mock<IHttpClientService>(),
                responseBody: "{\"success\":false,\"hostname\":null}");

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
            result.HostName.Should().BeEmpty();
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_OnHttpError()
        {
            var service = CreateService(new Mock<IHttpClientService>(), statusCode: HttpStatusCode.BadRequest);

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_OnHttpException()
        {
            var service = CreateService(new Mock<IHttpClientService>(),
                thrownByHttp: new HttpRequestException("boom"));

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_OnMalformedJson()
        {
            var service = CreateService(new Mock<IHttpClientService>(), responseBody: "not-json");

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_UsesConfigFactoryToResolveUri()
        {
            var factory = new Mock<IRecaptchaConfigFactory>();
            var stub = new StubRecaptchaConfig("https://configured.example/{0}");
            factory.Setup(f => f.GetRecaptchaConfig(It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(stub);

            var http = new Mock<IHttpClientService>();
            http.Setup(h => h.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string?>()))
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true,\"hostname\":\"h\"}")
                });

            var options = Options.Create(new CaptchaOptions());
            var service = new ReCaptchaVerificationService(
                http.Object, options, NullLogger<ReCaptchaVerificationService>.Instance, factory.Object);

            await service.VerifyAsync("tok");

            factory.Verify(f => f.GetRecaptchaConfig(It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
        }

        [Fact]
        public void Provider_IsRecaptcha()
        {
            var service = CreateService(new Mock<IHttpClientService>());
            service.Provider.Should().Be("recaptcha");
        }


        /// <summary>
        /// The tenant is configured but its secret could not be resolved. Calling Google without a
        /// secret could only ever come back unverified, and would leak the token to a pointless
        /// request, so the provider must not be called at all.
        /// </summary>
        [Fact]
        public async Task VerifyAsync_WhenTheConfigFactoryCannotProduceASecret_FailsClosedWithoutCallingTheProvider()
        {
            var httpClient = new Mock<IHttpClientService>();
            var configFactory = new Mock<IRecaptchaConfigFactory>();
            configFactory
                .Setup(f => f.GetRecaptchaConfig(It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync((IRecaptchaConfig?)null);

            var service = new ReCaptchaVerificationService(
                httpClient.Object,
                Options.Create(new CaptchaOptions()),
                NullLogger<ReCaptchaVerificationService>.Instance,
                configFactory.Object);

            var result = await service.VerifyAsync("captcha-token");

            result.Verified.Should().BeFalse();
            httpClient.Verify(
                h => h.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string?>()), Times.Never);
        }

        private sealed class StubRecaptchaConfig : IRecaptchaConfig
        {
            private readonly string _template;
            public StubRecaptchaConfig(string template) => _template = template;
            public string ResolveRecaptchaUri() => string.Format(System.Globalization.CultureInfo.InvariantCulture, _template, "stub");
        }
    }
}
