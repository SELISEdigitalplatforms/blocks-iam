using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StorageDriver;

namespace XUnitTest.Mfa
{
    /// <summary>
    /// Unit tests for <see cref="TotpService"/>. All collaborators are mocked; the generate, verify and
    /// per-user verify flows (including their guard and not-set-up branches) are exercised. A syntactically
    /// valid Base32 secret is used so the OtpNet verification path runs without throwing.
    /// </summary>
    public sealed class TotpServiceTests
    {
        private const string ValidBase32Secret = "JBSWY3DPEHPK3PXP";

        private readonly Mock<IMfaManagementRepository> _repository = new();
        private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IValidator<VerifyOtpRequest>> _validator = new();
        private readonly Mock<ITenants> _tenant = new();
        private readonly Mock<IStorageDriverService> _storage = new();

        private TotpService Sut() => new(
            _repository.Object, NullLogger<TotpService>.Instance, _httpContextAccessor.Object,
            _configuration.Object, _cacheClient.Object, _validator.Object, _tenant.Object, _storage.Object);

        [Fact]
        public async Task GenerateAsync_StoresUserAndReturnsMfaId()
        {
            _cacheClient.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(true);
            var result = await Sut().GenerateAsync(new UserInfo { ItemId = "u1" });
            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().NotBeNullOrWhiteSpace();
            _cacheClient.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), "u1", It.IsAny<long>()), Times.Once);
        }

        [Fact]
        public async Task VerifyAsync_InvalidRequest_ReturnsValidationErrors()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("MfaId", "required") }));
            var result = await Sut().VerifyAsync(new VerifyOtpRequest());
            result.Errors.Should().ContainKey("MfaId");
        }

        [Fact]
        public async Task VerifyAsync_SessionExpired_ReturnsError()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClient.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            var result = await Sut().VerifyAsync(new VerifyOtpRequest { MfaId = "mfa1" });
            result.Errors.Should().ContainKey("login_session_expired");
        }

        [Fact]
        public async Task VerifyAsync_TotpNotSetup_ReturnsError()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClient.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _cacheClient.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("u1");
            _repository.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);
            var result = await Sut().VerifyAsync(new VerifyOtpRequest { MfaId = "mfa1" });
            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyAsync_SetUp_ReturnsVerificationResult()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClient.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _cacheClient.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("u1");
            _repository.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { CreatedBy = "u1", Secret = ValidBase32Secret });
            var result = await Sut().VerifyAsync(new VerifyOtpRequest { MfaId = "mfa1", VerificationCode = "000000" });
            result.IsSuccess.Should().BeTrue();
            result.UserId.Should().Be("u1");
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyForUserAsync_MissingArgs_ReturnsError()
        {
            var result = await Sut().VerifyForUserAsync("", "");
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task VerifyForUserAsync_NotSetup_ReturnsError()
        {
            _repository.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);
            var result = await Sut().VerifyForUserAsync("u1", "123456");
            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyForUserAsync_SetUp_ReturnsVerificationResult()
        {
            _repository.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { CreatedBy = "u1", Secret = ValidBase32Secret });
            var result = await Sut().VerifyForUserAsync("u1", "000000");
            result.IsSuccess.Should().BeTrue();
            result.UserId.Should().Be("u1");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_UserNotExist_ReturnsError()
        {
            _repository.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserInfo?)null);
            var result = await Sut().GenerateTotpImageByUserAsync("u1");
            result.Errors.Should().ContainKey("user_not_exist");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_ExistingImage_ReturnsCached()
        {
            _repository.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "u1", Email = "u@x.com" });
            _repository.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { ImageUri = "https://img/qr.png", Secret = "SEC" });
            var result = await Sut().GenerateTotpImageByUserAsync("u1");
            result.IsSuccess.Should().BeTrue();
            result.QrImageUrl.Should().Be("https://img/qr.png");
            result.QrCode.Should().Be("SEC");
        }
    }
}
