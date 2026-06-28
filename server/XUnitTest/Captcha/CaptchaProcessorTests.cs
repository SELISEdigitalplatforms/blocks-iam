using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaProcessorTests
    {
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<ICaptchaVerificationServiceProvider> _captchaVerificationServiceProvider = new();
        private readonly Mock<ICaptchaVerificationService> _captchaVerificationService = new();
        private readonly CaptchaProcessor _processor;

        public CaptchaProcessorTests()
        {
            _processor = new CaptchaProcessor(
                _cacheClient.Object,
                _captchaVerificationServiceProvider.Object);
        }

        [Fact]
        public async Task SubmitAndCreateVerificationCodeAsync_ReturnsGuidInNFormat()
        {
            // Arrange
            var captchaId = "test-captcha-id";

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(captchaId))
                .ReturnsAsync(true);

            // Act
            var result = await _processor.SubmitAndCreateVerificationCodeAsync(captchaId);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().HaveLength(32); // GUID without hyphens
            result.Should().MatchRegex("^[a-f0-9]{32}$");
        }

        [Fact]
        public async Task SubmitAndCreateVerificationCodeAsync_StoresVerificationCodeInCache()
        {
            // Arrange
            var captchaId = "test-captcha-id";

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), "abc.com", 600))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(captchaId))
                .ReturnsAsync(true);

            // Act
            var result = await _processor.SubmitAndCreateVerificationCodeAsync(captchaId);

            // Assert
            _cacheClient.Verify(x => x.AddStringValueAsync(result, "abc.com", 600), Times.Once);
        }

        [Fact]
        public async Task SubmitAndCreateVerificationCodeAsync_RemovesCaptchaIdFromCache()
        {
            // Arrange
            var captchaId = "test-captcha-id";

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(captchaId))
                .ReturnsAsync(true);

            // Act
            await _processor.SubmitAndCreateVerificationCodeAsync(captchaId);

            // Assert
            _cacheClient.Verify(x => x.RemoveKeyAsync(captchaId), Times.Once);
        }

        [Fact]
        public async Task VerifyCaptchaAsync_WithValidCredentials_ReturnsVerificationResult()
        {
            // Arrange
            var configProvider = "test-provider";
            var verificationCode = "test-verification-code";
            var expectedResult = new VerificationResult
            {
                Verified = true,
                HostName = "test.com"
            };

            _captchaVerificationService
                .Setup(x => x.VerifyAsync(verificationCode))
                .ReturnsAsync(expectedResult);

            _captchaVerificationServiceProvider
                .Setup(x => x.GetCaptchaVerificationService(configProvider))
                .Returns(_captchaVerificationService.Object);

            // Act
            var result = await _processor.VerifyCaptchaAsync(configProvider, verificationCode);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(expectedResult);
            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("test.com");

            _captchaVerificationServiceProvider.Verify(
                x => x.GetCaptchaVerificationService(configProvider),
                Times.Once);
            _captchaVerificationService.Verify(
                x => x.VerifyAsync(verificationCode),
                Times.Once);
        }

        [Fact]
        public async Task VerifyCaptchaAsync_WithInvalidCredentials_ReturnsFailedVerification()
        {
            // Arrange
            var configProvider = "test-provider";
            var verificationCode = "invalid-code";
            var expectedResult = new VerificationResult
            {
                Verified = false,
                HostName = "",
                Errors = new Dictionary<string, string> { { "error", "Invalid verification code" } }
            };

            _captchaVerificationService
                .Setup(x => x.VerifyAsync(verificationCode))
                .ReturnsAsync(expectedResult);

            _captchaVerificationServiceProvider
                .Setup(x => x.GetCaptchaVerificationService(configProvider))
                .Returns(_captchaVerificationService.Object);

            // Act
            var result = await _processor.VerifyCaptchaAsync(configProvider, verificationCode);

            // Assert
            result.Should().NotBeNull();
            result.Verified.Should().BeFalse();
            result.HostName.Should().BeEmpty();
            result.Errors.Should().ContainKey("error");
        }
    }
}
