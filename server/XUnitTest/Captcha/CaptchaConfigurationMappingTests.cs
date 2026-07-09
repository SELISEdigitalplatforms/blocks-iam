using Blocks.CaptchaDriver;
using FluentAssertions;

namespace XUnitTest.Captcha
{
    public class CaptchaConfigurationMappingTests
    {
        [Fact]
        public void MapToCaptchaConfiguration_ReturnsNull_WhenSecretIsNull()
        {
            var result = CaptchaConfigurationMapping.MapToCaptchaConfiguration(null);
            result.Should().BeNull();
        }

        [Fact]
        public void MapToCaptchaConfiguration_ReturnsNull_WhenKeyValuePairsMissing()
        {
            var secret = new Secret { SecretKey = "captcha", KeyValuePairs = null! };
            var result = CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);
            result.Should().BeNull();
        }

        [Fact]
        public void MapToCaptchaConfiguration_DefaultsIsEnableToFalse_WhenKeyMissing()
        {
            var secret = new Secret
            {
                SecretKey = "captcha",
                KeyValuePairs = new Dictionary<string, string>
                {
                    { CaptchaSecretKeys.Provider, "recaptcha" }
                }
            };

            var config = CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);

            config.Should().NotBeNull();
            config!.IsEnable.Should().BeFalse();
        }

        [Fact]
        public void MapToCaptchaConfiguration_ParsesIsEnableTrue()
        {
            var secret = new Secret
            {
                SecretKey = "captcha",
                KeyValuePairs = new Dictionary<string, string>
                {
                    { CaptchaSecretKeys.IsEnable, "true" },
                    { CaptchaSecretKeys.Provider, "recaptcha" },
                    { CaptchaSecretKeys.CaptchaKey, "key" },
                    { CaptchaSecretKeys.CaptchaSecret, "secret" }
                }
            };

            var config = CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);

            config.Should().NotBeNull();
            config!.IsEnable.Should().BeTrue();
            config.Provider.Should().Be("recaptcha");
            config.CaptchaKey.Should().Be("key");
            config.CaptchaSecret.Should().Be("secret");
        }

        [Fact]
        public void MapToCaptchaConfiguration_TreatsInvalidIsEnableAsFalse()
        {
            var secret = new Secret
            {
                SecretKey = "captcha",
                KeyValuePairs = new Dictionary<string, string>
                {
                    { CaptchaSecretKeys.IsEnable, "yes" }
                }
            };

            var config = CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);
            config!.IsEnable.Should().BeFalse();
        }
    }
}
