using FluentAssertions;
using Mfa.DomainService.Shared;
using Mfa.DomainService.Validators;

namespace XUnitTest.Mfa.Shared.Validators
{
    public class VerifyOtpRequestValidatorTests
    {
        private readonly VerifyOtpRequestValidator _validator = new();

        [Fact]
        public async Task VerificationCode_Empty_Fails()
        {
            var request = new VerifyOtpRequest { VerificationCode = "", MfaId = "m1" };
            var result = await _validator.ValidateAsync(request);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.VerificationCodeRequired);
        }

        [Theory]
        [InlineData("12")]
        [InlineData("1234567")]
        public async Task VerificationCode_WrongLength_Fails(string code)
        {
            var request = new VerifyOtpRequest { VerificationCode = code, MfaId = "m1" };
            var result = await _validator.ValidateAsync(request);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.VerificationCodeLength);
        }

        [Fact]
        public async Task VerificationCode_NonNumeric_Fails()
        {
            var request = new VerifyOtpRequest { VerificationCode = "12ab56", MfaId = "m1" };
            var result = await _validator.ValidateAsync(request);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.VerificationCodeNumeric);
        }

        [Theory]
        [InlineData("1234")]
        [InlineData("123456")]
        public async Task VerificationCode_Valid_Passes(string code)
        {
            var request = new VerifyOtpRequest { VerificationCode = code, MfaId = "m1" };
            var result = await _validator.ValidateAsync(request);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task MfaId_Empty_Fails()
        {
            var request = new VerifyOtpRequest { VerificationCode = "1234", MfaId = "" };
            var result = await _validator.ValidateAsync(request);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.MfaRequired);
        }

        [Fact]
        public async Task MfaId_TooLong_Fails()
        {
            var request = new VerifyOtpRequest { VerificationCode = "1234", MfaId = new string('x', 51) };
            var result = await _validator.ValidateAsync(request);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == VerifyOtpRequestValidator.MfaMaxLimit);
        }

        [Fact]
        public async Task MfaId_Valid_Passes()
        {
            var request = new VerifyOtpRequest { VerificationCode = "1234", MfaId = "valid-id" };
            var result = await _validator.ValidateAsync(request);
            result.IsValid.Should().BeTrue();
        }
    }
}
