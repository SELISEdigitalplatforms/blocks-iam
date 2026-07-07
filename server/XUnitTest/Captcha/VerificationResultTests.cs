using Blocks.CaptchaDriver;
using FluentAssertions;

namespace XUnitTest.Captcha
{
    public class VerificationResultTests
    {
        [Fact]
        public void Default_IsUnverified()
        {
            var result = new VerificationResult();

            result.Verified.Should().BeFalse();
            result.HostName.Should().BeEmpty();
            result.Errors.Should().BeNull();
        }

        [Fact]
        public void ToVerifyCaptchaQueryResponse_MapsAllFields()
        {
            var errors = new Dictionary<string, string> { { "F", "E" } };
            var result = new VerificationResult
            {
                Verified = true,
                HostName = "site.example.com",
                Errors = errors
            };

            var response = result.ToVerifyCaptchaQueryResponse();

            response.Verified.Should().BeTrue();
            response.HostName.Should().Be("site.example.com");
            response.Errors.Should().BeSameAs(errors);
            response.IsSuccess.Should().BeTrue();
        }
    }
}
