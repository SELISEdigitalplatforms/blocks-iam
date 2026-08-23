using Blocks.CaptchaDriver;
using FluentAssertions;
using Moq;

namespace XUnitTest.Captcha
{
    /// <summary>
    /// Which secret a configuration verifies with, and the rule that the two sources never mix.
    /// </summary>
    public sealed class CaptchaConfigurationSecretExtensionsTests
    {
        private readonly Mock<ICaptchaSecretResolver> _resolver = new();

        [Fact]
        public async Task WithASecretId_ResolvesThroughTheVault()
        {
            _resolver.Setup(r => r.ResolveAsync("sec-1", It.IsAny<CancellationToken>()))
                     .ReturnsAsync("from-vault");
            var config = new CaptchaConfiguration { SecretId = "sec-1" };

            var result = await config.ResolveSecretAsync(_resolver.Object);

            result.Should().Be("from-vault");
        }

        [Fact]
        public async Task WithNoSecretId_UsesTheInlineLegacySecret()
        {
            var config = new CaptchaConfiguration { CaptchaSecret = "inline-secret" };

            var result = await config.ResolveSecretAsync(_resolver.Object);

            result.Should().Be("inline-secret");
            _resolver.Verify(
                r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The rule that keeps a new site key from being verified against a stale legacy secret.
        /// </summary>
        [Fact]
        public async Task WithASecretIdThatCannotBeResolved_FailsClosedAndIgnoresTheInlineSecret()
        {
            _resolver.Setup(r => r.ResolveAsync("sec-1", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((string?)null);
            var config = new CaptchaConfiguration
            {
                SecretId = "sec-1",
                CaptchaSecret = "stale-legacy-secret"
            };

            var result = await config.ResolveSecretAsync(_resolver.Object);

            result.Should().BeNull();
        }

        [Fact]
        public async Task WithNeitherSource_ReturnsNull()
        {
            var result = await new CaptchaConfiguration().ResolveSecretAsync(_resolver.Object);

            result.Should().BeNull();
        }
    }
}
