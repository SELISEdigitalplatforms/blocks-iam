using Blocks.CaptchaDriver;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaServiceTests
    {
        private static CaptchaService CreateService(
            out Mock<ICaptchaProcessor> processor,
            out Mock<ICaptchaConfigurationService> config,
            out Mock<IValidator<SubmitCaptchaRequest>> validator)
        {
            processor = new Mock<ICaptchaProcessor>();
            config = new Mock<ICaptchaConfigurationService>();
            validator = new Mock<IValidator<SubmitCaptchaRequest>>();
            return new CaptchaService(
                processor.Object,
                validator.Object,
                NullLogger<CaptchaService>.Instance,
                config.Object);
        }

        [Fact]
        public async Task SubmitCaptchaAsync_ReturnsValidationErrors()
        {
            var service = CreateService(out _, out _, out var validator);
            validator.Setup(v => v.ValidateAsync(It.IsAny<SubmitCaptchaRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FluentValidation.Results.ValidationResult(
                    new[] { new FluentValidation.Results.ValidationFailure("Id", "required") }));

            var result = await service.SubmitCaptchaAsync(new SubmitCaptchaRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Id");
        }

        [Fact]
        public async Task SubmitCaptchaAsync_ReturnsVerificationCode_OnValid()
        {
            var service = CreateService(out var processor, out _, out var validator);
            validator.Setup(v => v.ValidateAsync(It.IsAny<SubmitCaptchaRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FluentValidation.Results.ValidationResult());
            processor.Setup(p => p.SubmitAndCreateVerificationCodeAsync("id-1", "h.example"))
                .ReturnsAsync("code-1");

            var result = await service.SubmitCaptchaAsync(new SubmitCaptchaRequest
            {
                Id = "id-1",
                HostName = "h.example"
            });

            result.IsSuccess.Should().BeTrue();
            result.VerificationCode.Should().Be("code-1");
        }

        [Fact]
        public async Task VerifyCaptchaAsync_ReturnsError_OnEmptyCode()
        {
            var service = CreateService(out _, out _, out _);
            var result = await service.VerifyCaptchaAsync(new VerifyCaptchaRequest { VerificationCode = "" });
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
        }

        [Fact]
        public async Task VerifyCaptchaAsync_ReturnsError_WhenConfigNotFound()
        {
            var service = CreateService(out _, out var config, out _);
            config.Setup(c => c.GetByNameAsync("missing")).Returns(Task.FromResult<CaptchaConfiguration?>(null));

            var result = await service.VerifyCaptchaAsync(new VerifyCaptchaRequest
            {
                VerificationCode = "x",
                ConfigurationName = "missing"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Configuration");
        }

        [Fact]
        public async Task VerifyCaptchaAsync_ReturnsVerified_OnSuccess()
        {
            var service = CreateService(out var processor, out var config, out _);
            config.Setup(c => c.GetByNameAsync("recaptcha"))
                .Returns(Task.FromResult<CaptchaConfiguration?>(new CaptchaConfiguration { Provider = "recaptcha" }));
            processor.Setup(p => p.VerifyCaptchaAsync("recaptcha", "x"))
                .ReturnsAsync(new VerificationResult { Verified = true, HostName = "h.example" });

            var result = await service.VerifyCaptchaAsync(new VerifyCaptchaRequest
            {
                VerificationCode = "x",
                ConfigurationName = "recaptcha"
            });

            result.IsSuccess.Should().BeTrue();
            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("h.example");
        }

        [Fact]
        public async Task SubmitCaptchaAsync_ThrowsOnNull()
        {
            var service = CreateService(out _, out _, out _);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => service.SubmitCaptchaAsync(null!));
        }

        [Fact]
        public async Task VerifyCaptchaAsync_ThrowsOnNull()
        {
            var service = CreateService(out _, out _, out _);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => service.VerifyCaptchaAsync(null!));
        }
    }
}
