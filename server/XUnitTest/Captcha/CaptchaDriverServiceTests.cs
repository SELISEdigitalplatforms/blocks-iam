using Blocks.CaptchaDriver;
using FluentAssertions;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaDriverServiceTests
    {
        [Fact]
        public async Task Submit_DelegatesToCaptchaService()
        {
            var captchaService = new Mock<ICaptchaService>();
            var expected = new SubmitCaptchaRequestResponse(null) { IsSuccess = true, VerificationCode = "v" };
            captchaService.Setup(s => s.SubmitCaptchaAsync(It.IsAny<SubmitCaptchaRequest>()))
                .ReturnsAsync(expected);

            var driver = new CaptchaDriverService(captchaService.Object);
            var request = new SubmitCaptchaRequest { Id = "cap", HostName = "h" };

            var result = await driver.Submit(request);

            result.Should().Be(expected);
            captchaService.Verify(s => s.SubmitCaptchaAsync(request), Times.Once);
        }

        [Fact]
        public async Task Submit_ThrowsOnNull()
        {
            var driver = new CaptchaDriverService(new Mock<ICaptchaService>().Object);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => driver.Submit(null!));
        }

        [Fact]
        public async Task Verify_DelegatesToCaptchaService()
        {
            var captchaService = new Mock<ICaptchaService>();
            var expected = new VerifyCaptchaRequestResponse { IsSuccess = true, Verified = true };
            captchaService.Setup(s => s.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(expected);

            var driver = new CaptchaDriverService(captchaService.Object);
            var query = new VerifyCaptchaRequest { VerificationCode = "v", ConfigurationName = "n" };

            var result = await driver.Verify(query);

            result.Should().Be(expected);
            captchaService.Verify(s => s.VerifyCaptchaAsync(query), Times.Once);
        }

        [Fact]
        public async Task Verify_ThrowsOnNull()
        {
            var driver = new CaptchaDriverService(new Mock<ICaptchaService>().Object);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => driver.Verify(null!));
        }
    }
}
