using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.IamTests.Accounts.Validators
{
    public class RecoveryUserRequestValidatorTests
    {
        private readonly Mock<ICaptchaService> _captcha = new();
        private readonly Mock<IDbContextProvider> _dbContext = new();

        public RecoveryUserRequestValidatorTests()
        {
            _dbContext.Setup(d => d.GetCollection<Secret>("Secrets")).Returns(EmptySecrets());
        }

        private RecoveryUserRequestValidator Create() =>
            new(_captcha.Object, _dbContext.Object);

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
