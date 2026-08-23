using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Configurations;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Moq;

namespace XUnitTest.IamTests.Accounts.Validators
{
    public class ChangePasswordValidatorTests : IDisposable
    {
        private readonly Mock<IIamConfigurationRepository> _config = new();
        private readonly Mock<IIdentityAccessManagementRepository> _iamRepo = new();

        public ChangePasswordValidatorTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            // Minimum-length regex: passwords of at least 8 chars are "strong".
            _config.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = ".{8,}" });
            _iamRepo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>())).ReturnsAsync(false);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private ChangePasswordValidator Create() => new(_config.Object, _iamRepo.Object);

        private static ChangePasswordRequest Req(string oldPw = "OldPass123", string newPw = "NewPass123") =>
            new() { OldPassword = oldPw, NewPassword = newPw };

        [Fact]
        public async Task ValidRequest_Passes()
        {
            var result = await Create().ValidateAsync(Req());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyOldPassword_Fails()
        {
            var result = await Create().ValidateAsync(Req(oldPw: ""));
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "OldPassword");
        }

        [Fact]
        public async Task EmptyNewPassword_Fails()
        {
            var result = await Create().ValidateAsync(Req(newPw: ""));
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "NewPassword");
        }

        [Fact]
        public async Task WeakNewPassword_Fails()
        {
            var result = await Create().ValidateAsync(Req(newPw: "short")); // < 8 chars, fails the regex
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage.StartsWith("Password weak"));
        }

        [Fact]
        public async Task BlacklistedNewPassword_Fails()
        {
            _iamRepo.Setup(r => r.CheckPasswordBlackListedAsync("NewPass123")).ReturnsAsync(true);

            var result = await Create().ValidateAsync(Req());

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "This password can not be used.");
        }
    }
}

