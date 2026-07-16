using Authentication.DomainService.OAuth;
using Authentication.DomainService.RequestModel;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace XUnitTest.Auth.Oidc
{
    public class SaveOIDCClientRequestValidatorTests
    {
        private readonly SaveOIDCClientRequestValidator _validator = new();

        [Fact]
        public void DeviceFlow_WithCodeResponseType_IsRejected()
        {
            var request = new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = true,
                AllowedResponseTypes = new List<string> { "code" }
            };

            var result = _validator.TestValidate(request);
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void DeviceFlow_WithoutCodeResponseType_IsAccepted()
        {
            var request = new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = true,
                AllowedResponseTypes = new List<string>()
            };

            var result = _validator.TestValidate(request);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void NonDeviceFlow_WithCodeResponseType_IsAccepted()
        {
            var request = new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = false,
                AllowedResponseTypes = new List<string> { "code" }
            };

            var result = _validator.TestValidate(request);
            result.IsValid.Should().BeTrue();
        }
    }
}