using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using SignupUserRequest = Iam.DomainService.Accounts.SignupUserRequest;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MongoDB.Driver;
using XUnitTest.IamTests.Accounts.TestHelpers;

namespace XUnitTest.IamTests.Accounts.Services
{
    public class AccountServiceTests : IDisposable
    {
        private readonly Mock<ILogger<AccountService>> _loggerMock = new();
        private readonly Mock<IIdentityAccessManagementRepository> _repositoryMock = new();
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock = new();
        private readonly Mock<ICacheClient> _cacheClientMock = new();
        private readonly Mock<ITenants> _tenantsMock = new();
        private readonly Mock<IValidator<BaseAccountRequest>> _accountValidatorMock = new();
        private readonly Mock<IValidator<ChangePasswordRequest>> _changePasswordValidatorMock = new();
        private readonly Mock<IValidator<RecoveryUserRequest>> _recoverValidatorMock = new();
        private readonly Mock<IUserManagementMutationService> _userMutationMock = new();
        private readonly Mock<IResourceMutationService> _resourceMutationMock = new();
        private readonly Mock<ICaptchaService> _captchaServiceMock = new();
        private readonly Mock<IDbContextProvider> _dbContextProviderMock = new();

        public AccountServiceTests()
        {
            TestDataBuilder.InstallBlocksContext();
            SetupDefaultMocks();
        }

        public void Dispose()
        {
            TestDataBuilder.ResetBlocksContext();
        }

        private void SetupDefaultMocks()
        {
            _tenantsMock.Setup(t => t.GetTenantByID(It.IsAny<string>()))
                .Returns(new Tenant
                {
                    DbConnectionString = "x",
                    TenantSalt = TestDataBuilder.DefaultSalt,
                    JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
                });

            _iamServiceMock.Setup(s => s.HashPassword(It.IsAny<string>(), It.IsAny<string?>()))
                .Returns((string p, string? _) => "h:" + p);
            _iamServiceMock.Setup(s => s.VerifyPassword(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Returns((string p, string stored, string? _) => stored == "h:" + p);

            _cacheClientMock.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _cacheClientMock.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(true);
            _cacheClientMock.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string?)"user-1");
            _cacheClientMock.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            // Default user-lookup: nothing exists; specific tests override.
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateIamConfiguration());

            // By default, signup test path consults the captcha configuration collection.
            // Returning an empty (disabled) collection disables captcha checks.
            _dbContextProviderMock.Setup(d => d.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"))
                .Returns(MockMongoCollection(Array.Empty<CaptchaConfiguration>()).Object);
        }

        private AccountService CreateService(IHttpContextAccessor? http = null) =>
            new(
                _loggerMock.Object,
                _repositoryMock.Object,
                _iamServiceMock.Object,
                _cacheClientMock.Object,
                _tenantsMock.Object,
                _accountValidatorMock.Object,
                _changePasswordValidatorMock.Object,
                _recoverValidatorMock.Object,
                _userMutationMock.Object,
                _resourceMutationMock.Object,
                _captchaServiceMock.Object,
                _dbContextProviderMock.Object,
                http);

        private static Mock<IMongoCollection<T>> MockMongoCollection<T>(IEnumerable<T> items)
        {
            var mock = new Mock<IMongoCollection<T>>();
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            cursor.Setup(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true);

            mock.Setup(m => m.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T, T>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
            mock.Setup(m => m.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
            return mock;
        }

        #region Signup

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Signup_NullOrEmptyEmail_ReturnsError(string? email)
        {
            var service = CreateService();
            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = email! });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Email");
        }

        [Fact]
        public async Task Signup_SsoWithoutProvider_ReturnsError()
        {
            var service = CreateService();
            var result = await service.SignupAccountAsync(new SignupUserRequest
            {
                Email = "u@example.com",
                IsSsoSignup = true,
                Provider = string.Empty
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Provider");
        }

        [Fact]
        public async Task Signup_OrgFlagSetButOrgNameMissing_ReturnsError()
        {
            var service = CreateService();
            var result = await service.SignupAccountAsync(new SignupUserRequest
            {
                Email = "u@example.com",
                CreateOrganizationDuringSignup = true,
                OrganizationName = string.Empty
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("OrganizationName");
        }

        [Fact]
        public async Task Signup_TenantConfigMissing_ReturnsConfigurationError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration?)null);

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("signup_configuration");
        }

        [Fact]
        public async Task Signup_SsoDisabledByTenant_ReturnsDisabledError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration(isEmailPasswordSignUpEnabled: false, isSsoSignUpEnabled: false));

            var result = await service.SignupAccountAsync(new SignupUserRequest
            {
                Email = "u@example.com",
                IsSsoSignup = true,
                Provider = "google"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("signup_disabled");
        }

        [Fact]
        public async Task Signup_EmailDisabledByTenant_ReturnsDisabledError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration(isEmailPasswordSignUpEnabled: false));

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("signup_disabled");
        }

        [Fact]
        public async Task Signup_CaptchaEnabledWithoutCode_ReturnsCaptchaError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            var captchaColl = MockMongoCollection(new[] { new CaptchaConfiguration { IsEnable = true, Provider = "recaptcha" } });
            _dbContextProviderMock.Setup(d => d.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"))
                .Returns(captchaColl.Object);

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com", CaptchaCode = null });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("CaptchaCode");
        }

        [Fact]
        public async Task Signup_CaptchaEnabledWithInvalidCode_ReturnsCaptchaError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            var captchaColl = MockMongoCollection(new[] { new CaptchaConfiguration { IsEnable = true, Provider = "recaptcha" } });
            _dbContextProviderMock.Setup(d => d.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"))
                .Returns(captchaColl.Object);
            _captchaServiceMock.Setup(c => c.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = false });

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com", CaptchaCode = "wrong" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("CaptchaCode");
        }

        [Fact]
        public async Task Signup_CaptchaDisabled_SkipsCaptchaValidation()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(TestDataBuilder.CreateUser());

            var captchaColl = MockMongoCollection(Array.Empty<CaptchaConfiguration>());
            _dbContextProviderMock.Setup(d => d.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"))
                .Returns(captchaColl.Object);

            _userMutationMock.Setup(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { ItemId = "u-new", IsSuccess = true });
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("u-new");
        }

        [Fact]
        public async Task Signup_ExistingActiveVerifiedUser_ReturnsAlreadySignedUp()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(TestDataBuilder.CreateUser(active: true, isVerified: true));

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("already_signed_up");
        }

        [Fact]
        public async Task Signup_ExistingInactiveUser_SendsReactivation_Success()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(TestDataBuilder.CreateUser(active: false, isVerified: false));
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Signup_ExistingInactiveUser_ReactivationMailFails_ReturnsError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(TestDataBuilder.CreateUser(active: false, isVerified: false));
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("reactivation_failed");
        }

        [Fact]
        public async Task Signup_Email_NewUser_ActivationEmailFails_ReturnsActivationEmailFailed()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(TestDataBuilder.CreateUser());
            _userMutationMock.Setup(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { ItemId = "u-new", IsSuccess = true });
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("activation_email_failed");
        }

        [Fact]
        public async Task Signup_UserCreationFails_ReturnsCreationFailedError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userMutationMock.Setup(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "Email", "duplicate" } } });

            var result = await service.SignupAccountAsync(new SignupUserRequest { Email = "u@example.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Email");
        }

        [Fact]
        public async Task Signup_Sso_NewUser_CreatesUserFromSso_Success()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration(isSsoSignUpEnabled: true));
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userMutationMock.Setup(m => m.CreateUserFromSsoAsync(It.IsAny<CreateUserViaSsoRequest>()))
                .ReturnsAsync(new BaseMutationResponse { ItemId = "u-sso", IsSuccess = true });

            var result = await service.SignupAccountAsync(new SignupUserRequest
            {
                Email = "u@example.com",
                IsSsoSignup = true,
                Provider = "google",
                ExternalUserId = "ext-1"
            });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("u-sso");
        }

        [Fact]
        public async Task Signup_Sso_NewUser_SsoCreationFails_ReturnsError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration(isSsoSignUpEnabled: true));
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userMutationMock.Setup(m => m.CreateUserFromSsoAsync(It.IsAny<CreateUserViaSsoRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await service.SignupAccountAsync(new SignupUserRequest
            {
                Email = "u@example.com",
                IsSsoSignup = true,
                Provider = "google"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("sso_creation_failed");
        }

        [Fact]
        public async Task Signup_OrgCreateEnabled_OrgSucceeds_PropagatesOrgId()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _resourceMutationMock.Setup(r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string?>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "org-xyz" });
            _userMutationMock.Setup(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "u-new" });
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            _repositoryMock.Setup(r => r.GetUserByIdAsync("u-new")).ReturnsAsync(TestDataBuilder.CreateUser());

            var result = await service.SignupAccountAsync(new SignupUserRequest
            {
                Email = "u@example.com",
                CreateOrganizationDuringSignup = true,
                OrganizationName = "Acme",
                OrganizationDescription = "Org desc"
            });

            result.IsSuccess.Should().BeTrue();
            _resourceMutationMock.Verify(r => r.CreateOrganizationAsync(
                It.Is<CreateOrganizationRequest>(o => o.Name == "Acme" && o.CreatedFrom == CreatedFrom.ConstructSignup),
                It.IsAny<string?>()),
                Times.Once);
        }

        [Fact]
        public async Task Signup_OrgCreateEnabled_OrgFails_ReturnsOrgCreationFailed()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _resourceMutationMock.Setup(r => r.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string?>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "Name", "dup" } } });

            var result = await service.SignupAccountAsync(new SignupUserRequest
            {
                Email = "u@example.com",
                CreateOrganizationDuringSignup = true,
                OrganizationName = "Acme"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name");
        }

        #endregion

        #region Activate / ProcessActivation

        [Fact]
        public async Task Activate_InvalidModel_ReturnsValidationErrors()
        {
            var service = CreateService();
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Code", "err") }));

            var result = await service.ActivateAccountAsync(new ActivateUserRequest { Code = "c" });

            result.Errors.Should().ContainKey("Code");
        }

        [Fact]
        public async Task Activate_Valid_UpdatesUser_AndQueuesAccountActivityEvent()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser(active: false, isVerified: false);
            _cacheClientMock.Setup(c => c.GetStringValueAsync("code-1")).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var result = await service.ActivateAccountAsync(new ActivateUserRequest
            {
                Code = "code-1",
                FirstName = "F",
                LastName = "L",
                MailPurpose = "AccountActivated",
                PreventPostEvent = false
            });

            result.IsSuccess.Should().BeTrue();
            _cacheClientMock.Verify(c => c.RemoveKeyAsync("code-1"), Times.Once);
            _iamServiceMock.Verify(s => s.SendToQueueAsync(
                IdpConstants.IamQueue,
                It.Is<AccountActivityEvent>(e =>
                    e.Event == "Activate_Account" &&
                    e.UserId == user.ItemId &&
                    e.PreventPostEvent == false &&
                    e.MailPurpose == "AccountActivated")),
                Times.Once);
        }

        [Fact]
        public async Task Activate_PasswordProvided_RotatesHashAndStampAndTokenVersion()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser(active: false, isVerified: false);
            user.Password = "oldHashed";
            user.TokenVersion = 1;
            user.SecurityStamp = "old-stamp";
            user.FailedLoginCount = 3;
            _cacheClientMock.Setup(c => c.GetStringValueAsync("code-1")).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            await service.ActivateAccountAsync(new ActivateUserRequest
            {
                Code = "code-1",
                Password = "NewStrong1!"
            });

            _iamServiceMock.Verify(s => s.HashPassword("NewStrong1!", TestDataBuilder.DefaultSalt), Times.Once);
            _repositoryMock.Verify(r => r.UpdateUserAsync(It.Is<User>(u =>
                u.Password == "h:NewStrong1!" &&
                u.TokenVersion == 2 &&
                u.SecurityStamp != "old-stamp" &&
                u.FailedLoginCount == 0 &&
                u.LockoutUntilUtc == null)),
                Times.Once);
        }

        [Fact]
        public async Task Activate_UserNotFound_ReturnsFalse_AndDoesNotQueue()
        {
            var service = CreateService();
            _cacheClientMock.Setup(c => c.GetStringValueAsync("code-1")).ReturnsAsync("u-missing");
            _repositoryMock.Setup(r => r.GetUserByIdAsync("u-missing")).ReturnsAsync((User?)null);
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var result = await service.ActivateAccountAsync(new ActivateUserRequest { Code = "code-1" });

            result.IsSuccess.Should().BeFalse();
            _iamServiceMock.Verify(s => s.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>()), Times.Never);
        }

        [Fact]
        public async Task Activate_UpdateFails_DoesNotRemoveCache_AndDoesNotQueue()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser(active: false);
            _cacheClientMock.Setup(c => c.GetStringValueAsync("code-1")).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var result = await service.ActivateAccountAsync(new ActivateUserRequest { Code = "code-1" });

            result.IsSuccess.Should().BeFalse();
            _cacheClientMock.Verify(c => c.RemoveKeyAsync("code-1"), Times.Never);
            _iamServiceMock.Verify(s => s.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>()), Times.Never);
        }

        #endregion

        #region RecoverAccount

        [Fact]
        public async Task Recover_InvalidEmail_ReturnsErrors()
        {
            var service = CreateService();
            _recoverValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<RecoveryUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Email", "Invalid email format.") }));

            var result = await service.RecoverAccountAsync(new RecoveryUserRequest { Email = "bad" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Email");
        }

        [Fact]
        public async Task Recover_UserNotFound_ReturnsEmailNotAllowed()
        {
            var service = CreateService();
            _recoverValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<RecoveryUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await service.RecoverAccountAsync(new RecoveryUserRequest { Email = "miss@example.com" });

            result.Errors.Should().ContainKey("Email");
        }

        [Fact]
        public async Task Recover_Success_BuildsUrlAndAddsCacheAndInsertsUserKeyMap_AndSendsMail()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _recoverValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<RecoveryUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateIamConfiguration());
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>())).ReturnsAsync(true);

            var result = await service.RecoverAccountAsync(new RecoveryUserRequest { Email = "u@example.com", MailPurpose = "ResetPassword" });

            result.IsSuccess.Should().BeTrue();
            _cacheClientMock.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), user.ItemId, It.IsAny<long>()), Times.Once);
            _repositoryMock.Verify(r => r.InsertUserKeyMapAsync(It.Is<UserKeyMap>(k => k.UserId == user.ItemId)), Times.Once);
            _iamServiceMock.Verify(s => s.SendEmailAsync(It.IsAny<SendMail>()), Times.Once);
        }

        [Fact]
        public async Task SendActivationToEmailAsync_NormalizesEmailLower_AndSetsPurpose()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.Email = "USER@Example.COM";

            SendMail? captured = null;
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>()))
                .Callback<SendMail>(m => captured = m)
                .ReturnsAsync(true);

            await service.SendActivationToEmailAsync(user, "https://app.example.com/recover", "ResetPassword");

            captured.Should().NotBeNull();
            captured!.To.Should().Contain("user@example.com");
            captured.Purpose.Should().Be("ResetPassword");
        }

        [Fact]
        public async Task SendActivationToEmailAsync_DefaultsLanguageToEnUs_WhenUserLanguageMissing()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.Language = null;

            SendMail? captured = null;
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>()))
                .Callback<SendMail>(m => captured = m)
                .ReturnsAsync(true);

            await service.SendActivationToEmailAsync(user, "https://app.example.com/recover", "X");

            captured!.Language.Should().Be("en-US");
        }

        #endregion

        #region ResetPassword

        [Fact]
        public async Task Reset_InvalidModel_ReturnsErrors()
        {
            var service = CreateService();
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Code", "expired") }));

            var result = await service.ResetAccountPasswordAsync(new ResetPasswordRequest { Code = "x", Password = "p", LogoutFromAllDevices = false });

            result.Errors.Should().ContainKey("Code");
        }

        [Fact]
        public async Task Reset_UserNotFound_ReturnsFalse()
        {
            var service = CreateService();
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClientMock.Setup(c => c.GetStringValueAsync("x")).ReturnsAsync("u-gone");
            _repositoryMock.Setup(r => r.GetUserByIdAsync("u-gone")).ReturnsAsync((User?)null);

            var result = await service.ResetAccountPasswordAsync(new ResetPasswordRequest { Code = "x", Password = "p", LogoutFromAllDevices = true });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task Reset_Valid_UpdatesPassword_AndQueuesResetPasswordEvent_LogoutFromAllTrue()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.TokenVersion = 3;
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClientMock.Setup(c => c.GetStringValueAsync("x")).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            var result = await service.ResetAccountPasswordAsync(new ResetPasswordRequest
            {
                Code = "x",
                Password = "NewP@ssw0rd",
                LogoutFromAllDevices = true
            });

            result.IsSuccess.Should().BeTrue();
            _repositoryMock.Verify(r => r.UpdateUserAsync(It.Is<User>(u =>
                u.Password == "h:NewP@ssw0rd" && u.TokenVersion == 4)),
                Times.Once);
            _cacheClientMock.Verify(c => c.RemoveKeyAsync("x"), Times.Once);
            _iamServiceMock.Verify(s => s.SendToQueueAsync(
                IdpConstants.IamQueue,
                It.Is<AccountActivityEvent>(e =>
                    e.Event == "Reset_Password" &&
                    e.PreventPostEvent == false)),
                Times.Once);
        }

        [Fact]
        public async Task Reset_Valid_LogoutFromAllFalse_PreventPostEventTrue()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _accountValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<BaseAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClientMock.Setup(c => c.GetStringValueAsync("x")).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            await service.ResetAccountPasswordAsync(new ResetPasswordRequest
            {
                Code = "x",
                Password = "NewP@ssw0rd",
                LogoutFromAllDevices = false
            });

            _iamServiceMock.Verify(s => s.SendToQueueAsync(
                IdpConstants.IamQueue,
                It.Is<AccountActivityEvent>(e => e.PreventPostEvent == true)),
                Times.Once);
        }

        #endregion

        #region ChangePassword

        [Fact]
        public async Task Change_InvalidModel_ReturnsErrors()
        {
            var service = CreateService();
            _changePasswordValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("NewPassword", "weak") }));

            var result = await service.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = "o", NewPassword = "n" });

            result.Errors.Should().ContainKey("NewPassword");
        }

        [Fact]
        public async Task Change_UserNotFound_ReturnsFalse()
        {
            var service = CreateService();
            _changePasswordValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await service.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = "o", NewPassword = "n" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task Change_OldPasswordMismatch_ReturnsFalse()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.Password = "h:other"; // not h:OldP@ss
            TestDataBuilder.ResetBlocksContext();
            TestDataBuilder.InstallBlocksContext();

            _changePasswordValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);

            var result = await service.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = "OldP@ss", NewPassword = "NewP@ss" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task Change_Valid_QueuesChangePasswordEvent_PreventPostEventFalse_WhenLogoutOnPasswordChangeTrue()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.Password = "h:OldP@ss";
            _changePasswordValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateIamConfiguration(logoutOnPasswordChange: true));

            var result = await service.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = "OldP@ss", NewPassword = "NewP@ss1" });

            result.IsSuccess.Should().BeTrue();
            _iamServiceMock.Verify(s => s.SendToQueueAsync(
                IdpConstants.IamQueue,
                It.Is<AccountActivityEvent>(e => e.Event == "Change_Password" && e.PreventPostEvent == false)),
                Times.Once);
        }

        [Fact]
        public async Task Change_Valid_PreventPostEventTrue_WhenLogoutOnPasswordChangeFalse()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.Password = "h:OldP@ss";
            _changePasswordValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateIamConfiguration(logoutOnPasswordChange: false));

            await service.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = "OldP@ss", NewPassword = "NewP@ss1" });

            _iamServiceMock.Verify(s => s.SendToQueueAsync(
                IdpConstants.IamQueue,
                It.Is<AccountActivityEvent>(e => e.Event == "Change_Password" && e.PreventPostEvent == true)),
                Times.Once);
        }

        #endregion

        #region ResendActivation / SendReActivation

        [Fact]
        public async Task Resend_NullOrEmptyUserId_ReturnsUserIdRequired()
        {
            var service = CreateService();
            var result = await service.ResendActivationAsync(new ResendActivationRequest { UserId = string.Empty });
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Resend_UserNotFound_ReturnsFalseResponse()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetUserByIdAsync("u-x")).ReturnsAsync((User?)null);

            var result = await service.ResendActivationAsync(new ResendActivationRequest { UserId = "u-x" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task Resend_Success_CallsSendReActivation_AndReturnsIsSuccessTrue()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateIamConfiguration());
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var result = await service.ResendActivationAsync(new ResendActivationRequest { UserId = user.ItemId });

            result.IsSuccess.Should().BeTrue();
            _iamServiceMock.Verify(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SendReActivation_DefaultsMailPurpose_ToAccountActivation()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.MailPurpose = null;
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateIamConfiguration());
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);

            string? capturedPurpose = null;
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<User, string, string>((_, _, p) => capturedPurpose = p)
                .ReturnsAsync(true);

            await service.SendReActivationAsync(user);

            capturedPurpose.Should().Be("AccountActivation");
        }

        [Fact]
        public async Task SendReActivation_CachesKey_WithActivationLifetime()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateIamConfiguration(activationLifetimeMinutes: 30));
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            await service.SendReActivationAsync(user);

            _cacheClientMock.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), user.ItemId, (long)(30 * 60)), Times.Once);
        }

        [Fact]
        public async Task SendReActivation_InsertsUserKeyMap_WithSameExpireWindow()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            var config = TestDataBuilder.CreateIamConfiguration(activationLifetimeMinutes: 30);
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(config);
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            UserKeyMap? captured = null;
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()))
                .Callback<UserKeyMap>(k => captured = k)
                .ReturnsAsync(true);

            await service.SendReActivationAsync(user);

            captured.Should().NotBeNull();
            (captured!.ExpireDate - captured.IssueDate).TotalMinutes.Should().BeApproximately(30, 0.01);
        }

        #endregion

        #region ValidateActivationCode

        [Fact]
        public async Task Validate_NullOrEmptyCode_ReturnsActivationCodeRequired()
        {
            var service = CreateService();
            var result = await service.ValidateAccountActivationCodeAsync(new ValidateActivationCodeRequest { ActivationCode = string.Empty });
            result.Errors.Should().ContainKey("ActivationCode");
        }

        [Fact]
        public async Task Validate_CodeExistsInCache_ReturnsIsSuccessTrue_NoErrors()
        {
            var service = CreateService();
            _cacheClientMock.Setup(c => c.KeyExistsAsync("xyz")).ReturnsAsync(true);

            var result = await service.ValidateAccountActivationCodeAsync(new ValidateActivationCodeRequest { ActivationCode = "xyz" });

            result.IsSuccess.Should().BeTrue();
            result.Errors.Should().BeNull();
        }

        [Fact]
        public async Task Validate_CodeNotInCache_KeyMapUserIdEmpty_ReturnsInvalidActivationCode()
        {
            var service = CreateService();
            _cacheClientMock.Setup(c => c.KeyExistsAsync("xyz")).ReturnsAsync(false);
            _repositoryMock.Setup(r => r.GetUserIdFromKeyMapByKeyAsync("xyz")).ReturnsAsync(string.Empty);

            var result = await service.ValidateAccountActivationCodeAsync(new ValidateActivationCodeRequest { ActivationCode = "xyz" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ActivationCode");
        }

        [Fact]
        public async Task Validate_CodeNotInCache_KeyMapUserIdPresent_ReturnsIsSuccessTrue_WithUserId()
        {
            var service = CreateService();
            _cacheClientMock.Setup(c => c.KeyExistsAsync("xyz")).ReturnsAsync(false);
            _repositoryMock.Setup(r => r.GetUserIdFromKeyMapByKeyAsync("xyz")).ReturnsAsync("u-99");

            var result = await service.ValidateAccountActivationCodeAsync(new ValidateActivationCodeRequest { ActivationCode = "xyz" });

            result.IsSuccess.Should().BeTrue();
            result.UserId.Should().Be("u-99");
        }

        [Fact]
        public async Task Recover_UrlBuildFails_ReturnsFalse_AndDoesNotCache()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();

            _recoverValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<RecoveryUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration
                {
                    IsOidcEnabled = false,
                    AccountActivationPath = string.Empty, // forces TryBuildUserActionUrl to return false
                    RecoverAccountPath = string.Empty,
                    AccountActionBaseUrl = string.Empty,
                    UseAccountActionBaseUrlAsDefault = true
                });

            // Trigger ProcessRecoverAccountAsync directly to keep the path deterministic.
            var result = await service.ProcessRecoverAccountAsync(user, "RecoverAccount");

            result.Should().BeFalse();
            _cacheClientMock.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
            _repositoryMock.Verify(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()), Times.Never);
        }

        [Fact]
        public async Task SendReActivation_UrlBuildFails_ReturnsFalse_AndDoesNotCache()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration
                {
                    IsOidcEnabled = false,
                    AccountActionBaseUrl = string.Empty,
                    UseAccountActionBaseUrlAsDefault = true,
                    AccountActivationPath = string.Empty
                });

            var result = await service.SendReActivationAsync(user);

            result.Should().BeFalse();
            _cacheClientMock.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
            _repositoryMock.Verify(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>()), Times.Never);
        }

        [Fact]
        public async Task SendReActivation_OidcEnabled_UsesOidcActivateRoute()
        {
            // Provide HTTP context so IamHelper.TryBuildUserActionUrl succeeds in OIDC mode.
            var http = new Mock<IHttpContextAccessor>();
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("app.example.com");
            http.Setup(h => h.HttpContext).Returns(httpContext);

            var service = CreateService(http.Object);
            var user = TestDataBuilder.CreateUser();
            _repositoryMock.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration
                {
                    IsOidcEnabled = true,
                    AccountActionBaseUrl = string.Empty,
                    UseAccountActionBaseUrlAsDefault = true,
                    AccountActivationPath = "Account/Activate"
                });
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            // Provide a tenant id via BlocksContext to exercise the OIDC branch.
            TestDataBuilder.ResetBlocksContext();
            TestDataBuilder.InstallBlocksContext(tenantId: "tenant-xyz");
            var result = await service.SendReActivationAsync(user);

            result.Should().BeTrue();
        }

        #endregion

        #region SaveSignUpSetting / GetSignUpSetting

        [Fact]
        public async Task Save_TenantConfigMissing_ReturnsSignUpSettingExistError()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration?)null);

            var result = await service.SaveSignUpSettingAsync(new SaveSignUpSettingRequest
            {
                IsEmailPasswordSignUpEnabled = true,
                IsSSoSignUpEnabled = false
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("sign_up_setting_exist");
        }

        [Fact]
        public async Task Save_TenantConfigPresent_Persists_AndReturnsItemId()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.SaveSignUpSettingAsync(It.IsAny<TenantConfiguration>())).Returns(Task.CompletedTask);

            var result = await service.SaveSignUpSettingAsync(new SaveSignUpSettingRequest
            {
                IsEmailPasswordSignUpEnabled = true,
                IsSSoSignUpEnabled = true
            });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be(TestDataBuilder.CreateTenantConfiguration().ItemId);
        }

        [Fact]
        public async Task Get_TenantConfigMissing_ReturnsAllDisabled()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration?)null);

            var result = await service.GetSignUpSettingAsync();

            result.Should().ContainKey("isSignUpEnable");
            ((bool)result["isSignUpEnable"]).Should().BeFalse();
        }

        [Fact]
        public async Task Get_TenantConfigPresent_EmailAndSsoDisabled_ReturnsIsSignUpEnableFalse()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration(isEmailPasswordSignUpEnabled: false, isSsoSignUpEnabled: false));

            var result = await service.GetSignUpSettingAsync();

            ((bool)result["isSignUpEnable"]).Should().BeFalse();
            ((bool)result["isEmailPasswordSignUpEnabled"]).Should().BeFalse();
            ((bool)result["isSSoSignUpEnabled"]).Should().BeFalse();
        }

        [Fact]
        public async Task Get_TenantConfigPresent_EmailEnabled_ReturnsCorrespondingFlags()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(TestDataBuilder.CreateTenantConfiguration(isEmailPasswordSignUpEnabled: true, isSsoSignUpEnabled: false));

            var result = await service.GetSignUpSettingAsync();

            ((bool)result["isSignUpEnable"]).Should().BeTrue();
            ((bool)result["isEmailPasswordSignUpEnabled"]).Should().BeTrue();
            ((bool)result["isSSoSignUpEnabled"]).Should().BeFalse();
        }

        #endregion

        #region UnlockAccount

        [Fact]
        public async Task Unlock_NullOrEmptyUserId_ReturnsUserIdRequired()
        {
            var service = CreateService();
            var result = await service.UnlockAccountAsync(string.Empty);
            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Unlock_UserNotFound_ReturnsUserNotFound()
        {
            var service = CreateService();
            _repositoryMock.Setup(r => r.GetUserByIdAsync("u-x")).ReturnsAsync((User?)null);

            var result = await service.UnlockAccountAsync("u-x");

            result.Errors.Should().ContainKey("UserId");
        }

        [Fact]
        public async Task Unlock_Success_ResetsCounters_AndSendsUnlockedEmail()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.FailedLoginCount = 5;
            user.LockoutCount = 2;
            user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            user.LastFailedLoginUtc = DateTime.UtcNow;
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            var result = await service.UnlockAccountAsync(user.ItemId);

            result.IsSuccess.Should().BeTrue();
            _repositoryMock.Verify(r => r.UpdateUserAsync(It.Is<User>(u =>
                u.FailedLoginCount == 0 &&
                u.LockoutCount == 0 &&
                u.LockoutUntilUtc == null &&
                u.LastFailedLoginUtc == null)),
                Times.Once);
            _iamServiceMock.Verify(s => s.SendEmailAsync(It.IsAny<SendMail>()), Times.Once);
        }

        [Fact]
        public async Task Unlock_EmailThrows_StillReturnsIsSuccessTrue()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>())).ThrowsAsync(new Exception("boom"));

            var result = await service.UnlockAccountAsync(user.ItemId);

            result.IsSuccess.Should().BeTrue();
        }

        #endregion

        #region SendAccountLocked / Unlocked Notifications

        [Fact]
        public async Task SendLocked_NullUser_Throws()
        {
            var service = CreateService();
            var act = async () => await service.SendAccountLockedNotificationAsync(null!, DateTime.UtcNow);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task SendLocked_NoEmail_SkipsSending()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.Email = null;

            await service.SendAccountLockedNotificationAsync(user, DateTime.UtcNow);

            _iamServiceMock.Verify(s => s.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
        }

        [Fact]
        public async Task SendLocked_WithEmail_SendsMail_WithLockedPurpose()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            SendMail? captured = null;
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>()))
                .Callback<SendMail>(m => captured = m)
                .ReturnsAsync(true);

            await service.SendAccountLockedNotificationAsync(user, DateTime.UtcNow);

            captured.Should().NotBeNull();
            captured!.Purpose.Should().Be("AccountLockedNotification");
        }

        [Fact]
        public async Task SendLocked_EmailThrows_DoesNotRethrow()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>())).ThrowsAsync(new Exception("boom"));

            var act = async () => await service.SendAccountLockedNotificationAsync(user, DateTime.UtcNow);
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task SendUnlocked_NullUser_Throws()
        {
            var service = CreateService();
            var act = async () => await service.SendAccountUnlockedNotificationAsync(null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task SendUnlocked_NoEmail_SkipsSending()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            user.Email = null;

            await service.SendAccountUnlockedNotificationAsync(user);

            _iamServiceMock.Verify(s => s.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
        }

        [Fact]
        public async Task SendUnlocked_WithEmail_SendsMail_WithUnlockedPurpose()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            SendMail? captured = null;
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>()))
                .Callback<SendMail>(m => captured = m)
                .ReturnsAsync(true);

            await service.SendAccountUnlockedNotificationAsync(user);

            captured.Should().NotBeNull();
            captured!.Purpose.Should().Be("AccountUnlockedNotification");
        }

        [Fact]
        public async Task SendUnlocked_EmailThrows_DoesNotRethrow()
        {
            var service = CreateService();
            var user = TestDataBuilder.CreateUser();
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>())).ThrowsAsync(new Exception("boom"));

            var act = async () => await service.SendAccountUnlockedNotificationAsync(user);
            await act.Should().NotThrowAsync();
        }

        #endregion
    }
}

