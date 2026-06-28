using Blocks.CaptchaDriver;
using FluentAssertions;

namespace XUnitTest.Captcha
{
    public class SubmitCaptchaCommandValidatorTests
    {
        private readonly SubmitCaptchaCommandValidator _validator;

        public SubmitCaptchaCommandValidatorTests()
        {
            _validator = new SubmitCaptchaCommandValidator();
        }

        [Fact]
        public async Task ValidateAsync_WithValidId_ReturnsSuccessResult()
        {
            // Arrange
            var request = new SubmitCaptchaRequest { Id = "captcha-123" };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ValidateAsync_WithNullOrEmptyId_ReturnsValidationError(string id)
        {
            // Arrange
            var request = new SubmitCaptchaRequest { Id = id };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Id");
        }
    }
}
