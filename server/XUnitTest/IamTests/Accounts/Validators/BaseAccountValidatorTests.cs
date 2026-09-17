using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Configurations;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.IamTests.Accounts.Validators
{
    public class BaseAccountValidatorTests : IDisposable
    {
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IIamConfigurationRepository> _configRepo = new();
        private readonly Mock<IIdentityAccessManagementRepository> _iamRepo = new();
        private readonly Mock<ICaptchaService> _captcha = new();
        private readonly Mock<ICaptchaConfigurationService> _captchaConfig = new();

        public BaseAccountValidatorTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "user-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            // Defaults: code registered, no password regex, not blacklisted, empty secrets collection.
            _cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = string.Empty });
            _iamRepo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            // No captcha configuration in either store; the captcha rule still delegates to
            // ICaptchaService, which the individual tests set up.
            _captchaConfig.Setup(c => c.GetCaptchaConfigurationAsync())
                .ReturnsAsync((CaptchaConfiguration?)null);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private BaseAccountValidator Create() =>
            new(_cache.Object, _configRepo.Object, _iamRepo.Object, _captcha.Object, _captchaConfig.Object);


        [Fact]
        public async Task Valid_Code_NoPassword_NoCaptcha_Passes()
        {
            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "valid-code" });
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Code_Empty_Fails()
        {
            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Code");
        }

        [Fact]
        public async Task Code_NotRegistered_Fails_WithExpiredMessage()
        {
            _cache.Setup(c => c.KeyExistsAsync("stale-code")).ReturnsAsync(false);

            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "stale-code" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage == "The code has expired. Please request a new one to continue");
        }

        [Fact]
        public async Task Password_Weak_Fails()
        {
            // A min-length regex fails regardless of casing (RegexOptions.IgnoreCase is applied).
            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = "^.{8,}$" });

            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "valid-code", Password = "short" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Does not meet project's password requirements");
        }

        [Fact]
        public async Task Password_Blacklisted_Fails()
        {
            _iamRepo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "valid-code", Password = "Str0ng!Passw0rd" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "This password can not be used.");
        }

        [Fact]
        public async Task Password_BlacklistLookupFails_PropagatesInsteadOfAllowing()
        {
            // Fail closed on the account flows too: a root-database outage must fail the request
            // rather than let an unchecked password through.
            _iamRepo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>()))
                .ThrowsAsync(new TimeoutException("root database unreachable"));

            var act = async () => await Create().ValidateAsync(
                new BaseAccountRequest { Code = "valid-code", Password = "Str0ng!Passw0rd" });

            await act.Should().ThrowAsync<TimeoutException>();
        }

        [Fact]
        public async Task Password_Strong_AndNotBlacklisted_Passes()
        {
            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "valid-code", Password = "Str0ng!Passw0rd" });
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Captcha_Mismatch_Fails()
        {
            _captcha.Setup(c => c.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = false });

            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "valid-code", CaptchaCode = "abc" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Captcha doesn't match");
        }

        [Fact]
        public async Task Captcha_Match_Passes()
        {
            _captcha.Setup(c => c.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = true });

            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "valid-code", CaptchaCode = "abc" });

            result.IsValid.Should().BeTrue();
        }

        // The rule a reset-password / activation submit actually runs: ResetAccountPasswordAsync
        // and ActivateAccountAsync both validate through this validator before touching the
        // account, and it calls the same PasswordStrengthEvaluator that /api/idp/password-check
        // uses. These pin that a password failing the tenant regex is rejected here, with the
        // message the screens show.
        private const string TenantRegex =
            @"^(?=.{10,32}$)(?!.*(.)\1{2})(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[!@#$%^&*])\S+$";

        [Theory]
        [InlineData("Aaa@1234567")]   // "Aaa" is three of the same character: the regex is matched case-insensitively
        [InlineData("Abc@123")]       // shorter than 10
        [InlineData("Abcdefghijk!")]  // no digit
        [InlineData("Abc(1234567")]   // "(" is not one of !@#$%^&*
        [InlineData("Abc @1234567")]  // \S+ forbids whitespace
        public async Task Password_FailingTheTenantRegex_IsRejectedWithTheProjectMessage(string password)
        {
            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = TenantRegex });

            var result = await Create().ValidateAsync(
                new BaseAccountRequest { Code = "valid-code", Password = password });

            result.IsValid.Should().BeFalse(because: $"'{password}' does not satisfy the tenant rule");
            result.Errors.Should().Contain(e =>
                e.PropertyName == "Password"
                && e.ErrorMessage == "Does not meet project's password requirements");
        }

        [Theory]
        // KNOWN DEFECT, pinned so it cannot change unnoticed: PasswordStrengthEvaluator matches
        // with RegexOptions.IgnoreCase, which makes "(?=.*[A-Z])" and "(?=.*[a-z])" vacuous --
        // an all-lowercase password satisfies the uppercase requirement and vice versa. Dropping
        // IgnoreCase would fix it, but it also tightens every tenant at once, and it would flip
        // "Aaa@1234567" above to accepted (case-sensitively "Aa" is not a repeated character).
        [InlineData("abc@1234567")]
        [InlineData("ABC@1234567")]
        public async Task Password_CaseRequirementsAreNotActuallyEnforced(string password)
        {
            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = TenantRegex });

            var result = await Create().ValidateAsync(
                new BaseAccountRequest { Code = "valid-code", Password = password });

            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("Abc@1234567")]
        [InlineData("Xy7#mLp2$wZk9")]
        public async Task Password_SatisfyingTheTenantRegex_IsAccepted(string password)
        {
            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = TenantRegex });

            var result = await Create().ValidateAsync(
                new BaseAccountRequest { Code = "valid-code", Password = password });

            result.IsValid.Should().BeTrue();
        }

    }
}
