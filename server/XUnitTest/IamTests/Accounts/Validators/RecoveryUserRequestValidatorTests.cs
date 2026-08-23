using Blocks.CaptchaDriver;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Moq;

namespace XUnitTest.IamTests.Accounts.Validators
{
    public class RecoveryUserRequestValidatorTests
    {
        private readonly Mock<ICaptchaService> _captcha = new();
        private readonly Mock<ICaptchaConfigurationService> _captchaConfig = new();

        public RecoveryUserRequestValidatorTests()
        {
            // No configuration in either store. The validator only reads the provider name from
            // it, so the captcha rule still runs and delegates to ICaptchaService.
            _captchaConfig.Setup(c => c.GetCaptchaConfigurationAsync())
                .ReturnsAsync((CaptchaConfiguration?)null);
        }

        private RecoveryUserRequestValidator Create() =>
            new(_captcha.Object, _captchaConfig.Object);

        [Fact]
        public async Task ValidEmail_NoCaptcha_Passes()
        {
            var result = await Create().ValidateAsync(new RecoveryUserRequest { Email = "user@example.com" });
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Email_Empty_Fails()
        {
            var result = await Create().ValidateAsync(new RecoveryUserRequest { Email = "" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Email is required.");
        }

        [Fact]
        public async Task Email_InvalidFormat_Fails()
        {
            var result = await Create().ValidateAsync(new RecoveryUserRequest { Email = "not-an-email" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid email format.");
        }

        [Fact]
        public async Task Captcha_Mismatch_Fails()
        {
            _captcha.Setup(c => c.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = false });

            var result = await Create().ValidateAsync(new RecoveryUserRequest { Email = "user@example.com", CaptchaCode = "abc" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Captcha doesn't match");
        }

        [Fact]
        public async Task Captcha_Match_Passes()
        {
            _captcha.Setup(c => c.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = true });

            var result = await Create().ValidateAsync(new RecoveryUserRequest { Email = "user@example.com", CaptchaCode = "abc" });

            result.IsValid.Should().BeTrue();
        }
    }
}
