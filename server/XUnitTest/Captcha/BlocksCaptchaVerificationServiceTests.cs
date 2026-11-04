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
            cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
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

        [Theory]
        [InlineData("oidc_context:state123")]
        [InlineData("session:abc")]
        public async Task VerifyAsync_RefusesForeignRedisKey_AndNeverReadsIt(string foreignKey)
        {
            var service = CreateService(out var cache);
            cache.Setup(c => c.GetStringValueAsync(foreignKey)).ReturnsAsync("secret-context");

            var result = await service.VerifyAsync(foreignKey);

            result.Verified.Should().BeFalse();
            cache.Verify(c => c.GetStringValueAsync(foreignKey), Times.Never);
            cache.Verify(c => c.RemoveKeyAsync(foreignKey), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_AcceptsIssuedRawCode_AndConsumesPrefixedKey()
        {
            var service = CreateService(out var cache);
            cache.Setup(c => c.GetStringValueAsync("captcha:vc:abc")).ReturnsAsync("site.example.com");

            var result = await service.VerifyAsync("abc");

            result.Verified.Should().BeTrue();
            cache.Verify(c => c.RemoveKeyAsync("captcha:vc:abc"), Times.Once);
        }

        [Fact]
        public async Task VerifyAsync_RefusesReplay()
        {
            var service = CreateService(out var cache);
            var store = new Dictionary<string, string> { ["captcha:vc:abc"] = "site.example.com" };
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string k) => store.GetValueOrDefault(k));
            cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync((string k) => store.Remove(k));

            (await service.VerifyAsync("abc")).Verified.Should().BeTrue();
            (await service.VerifyAsync("abc")).Verified.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_ParallelUseOfOneCode_OnlyTheRequestThatDeletedItPasses()
        {
            var service = CreateService(out var cache);
            cache.Setup(c => c.GetStringValueAsync("captcha:vc:abc")).ReturnsAsync("site.example.com");
            cache.SetupSequence(c => c.RemoveKeyAsync("captcha:vc:abc")).ReturnsAsync(true).ReturnsAsync(false);

            (await service.VerifyAsync("abc")).Verified.Should().BeTrue();
            (await service.VerifyAsync("abc")).Verified.Should().BeFalse();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("captcha:vc:")]
        public async Task VerifyAsync_RefusesEmptyOrWhitespace(string code)
        {
            var service = CreateService(out var cache);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("site.example.com");

            var result = await service.VerifyAsync(code);

            result.Verified.Should().BeFalse();
            cache.Verify(c => c.GetStringValueAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_Throws_OnNull()
        {
            var service = CreateService(out _);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => service.VerifyAsync(null!));
        }
    }
}
