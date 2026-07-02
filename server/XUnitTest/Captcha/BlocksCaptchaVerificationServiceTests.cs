using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Captcha
{
    public class BlocksCaptchaVerificationServiceTests
    {
        private static BlocksCaptchaVerificationService CreateService(out Mock<ICacheClient> cache)
        {
            cache = new Mock<ICacheClient>();
            return new BlocksCaptchaVerificationService(cache.Object);
        }

        [Fact]
        public async Task VerifyAsync_ReturnsFailure_WhenCacheMiss()
        {
            var service = CreateService(out var cache);
            cache.Setup(c => c.GetStringValueAsync("captcha:vc:abc")).Returns(Task.FromResult<string?>(null!));

            var result = await service.VerifyAsync("captcha:vc:abc");

            result.Verified.Should().BeFalse();
            result.HostName.Should().BeEmpty();
            result.Errors.Should().ContainKey("VerificationCode");
        }

        [Fact]
        public async Task VerifyAsync_ReturnsSuccess_AndRemovesKey_WhenCacheHit()
        {
            var service = CreateService(out var cache);
            cache.Setup(c => c.GetStringValueAsync("captcha:vc:abc")).ReturnsAsync("site.example.com");

            var result = await service.VerifyAsync("captcha:vc:abc");

            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("site.example.com");
            cache.Verify(c => c.RemoveKeyAsync("captcha:vc:abc"), Times.Once);
        }

        [Fact]
        public async Task VerifyAsync_Throws_OnNull()
        {
            var service = CreateService(out _);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => service.VerifyAsync(null!));
        }
    }
}
