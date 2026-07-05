using Blocks.CaptchaDriver;
using FluentAssertions;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaConfigurationServiceTests
    {
        [Fact]
        public async Task GetByNameAsync_DelegatesToRepository()
        {
            var repo = new Mock<ICaptchaConfigurationRepository>();
            var config = new CaptchaConfiguration { Provider = "recaptcha" };
            repo.Setup(r => r.GetByProviderAsync("recaptcha"))
                .Returns(Task.FromResult<CaptchaConfiguration?>(config));

            var service = new CaptchaConfigurationService(repo.Object);
            var result = await service.GetByNameAsync("recaptcha");

            result.Should().Be(config);
            repo.Verify(r => r.GetByProviderAsync("recaptcha"), Times.Once);
        }

        [Fact]
        public async Task GetByNameAsync_ThrowsOnNull()
        {
            var service = new CaptchaConfigurationService(new Mock<ICaptchaConfigurationRepository>().Object);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => service.GetByNameAsync(null!));
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_DelegatesToRepository()
        {
            var repo = new Mock<ICaptchaConfigurationRepository>();
            var config = new CaptchaConfiguration { IsEnable = true };
            repo.Setup(r => r.GetCaptchaConfigurationAsync())
                .Returns(Task.FromResult<CaptchaConfiguration?>(config));

            var service = new CaptchaConfigurationService(repo.Object);
            var result = await service.GetCaptchaConfigurationAsync();

            result.Should().Be(config);
            repo.Verify(r => r.GetCaptchaConfigurationAsync(), Times.Once);
        }
    }
}
