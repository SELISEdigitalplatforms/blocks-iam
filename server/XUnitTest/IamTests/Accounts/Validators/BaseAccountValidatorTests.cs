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
        private readonly Mock<IDbContextProvider> _dbContext = new();

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
            _iamRepo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _dbContext.Setup(d => d.GetCollection<Secret>("Secrets")).Returns(EmptySecrets());
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private BaseAccountValidator Create() =>
            new(_cache.Object, _configRepo.Object, _iamRepo.Object, _captcha.Object, _dbContext.Object);

        private static IMongoCollection<Secret> EmptySecrets()
        {
            var cursor = new Mock<IAsyncCursor<Secret>>();
            cursor.Setup(c => c.Current).Returns(new List<Secret>());
            cursor.Setup(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(false);
            cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var collection = new Mock<IMongoCollection<Secret>>();
            collection.Setup(m => m.FindAsync(
                    It.IsAny<FilterDefinition<Secret>>(),
                    It.IsAny<FindOptions<Secret, Secret>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
            return collection.Object;
        }

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
            result.Errors.Should().Contain(e => e.ErrorMessage.StartsWith("Password is weak"));
        }

        [Fact]
        public async Task Password_Blacklisted_Fails()
        {
            _iamRepo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var result = await Create().ValidateAsync(new BaseAccountRequest { Code = "valid-code", Password = "Str0ng!Passw0rd" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "This password can not be used.");
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
    }
}
