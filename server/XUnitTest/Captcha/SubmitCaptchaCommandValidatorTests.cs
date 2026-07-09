using Blocks.CaptchaDriver;
using FluentAssertions;

namespace XUnitTest.Captcha
{
    public class SubmitCaptchaCommandValidatorTests
    {
        private readonly SubmitCaptchaCommandValidator _validator = new();

        [Fact]
        public async Task Valid_Passes()
        {
            var result = await _validator.ValidateAsync(new SubmitCaptchaRequest
            {
                Id = "captcha-1",
                HostName = "site.example.com",
                Value = "user-supplied-value"
            });
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task MissingId_Fails()
        {
            var result = await _validator.ValidateAsync(new SubmitCaptchaRequest
            {
                Id = string.Empty,
                HostName = "site.example.com"
            });
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Id");
        }

        [Fact]
        public async Task MissingHostName_Fails()
        {
            var result = await _validator.ValidateAsync(new SubmitCaptchaRequest
            {
                Id = "captcha-1",
                HostName = string.Empty
            });
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "HostName");
        }
    }
}
