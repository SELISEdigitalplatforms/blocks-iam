using System.Text.Json;
using Blocks.CaptchaDriver;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation.Results;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users.RequestModel;
using Moq;
using XUnitTest.IamTests.Accounts.TestHelpers;
using SignupUserRequest = Iam.DomainService.Accounts.SignupUserRequest;
using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace XUnitTest.IamTests.Accounts.Services
{
    /// <summary>
    /// Pentest M6: /auth/signup and /auth/recover give one answer whether or not the email has
    /// an account, and whatever state that account is in. Only the mail differs.
    /// </summary>
    public partial class AccountServiceTests
    {
        public static TheoryData<string> AccountKinds => new() { "new", "active", "pending", "disabled", "suspended", "deactivated" };

        private User? AccountOf(string kind) => kind switch
        {
            "new" => null,
            "active" => TestDataBuilder.CreateUser(active: true, isVerified: true),
            "pending" => TestDataBuilder.CreateUser(active: false, isVerified: false),
            "disabled" => WithStatus(TestDataBuilder.CreateUser(active: false), UserLifecycleStatus.Disabled),
            "suspended" => WithStatus(TestDataBuilder.CreateUser(active: true), UserLifecycleStatus.Suspended),
            "deactivated" => Deactivated(TestDataBuilder.CreateUser(active: false)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private static User WithStatus(User user, UserLifecycleStatus status)
        {
            user.Status = status;
            return user;
        }

        private static User Deactivated(User user)
        {
            user.DeactivatedAtUtc = DateTime.UtcNow.AddDays(-1);
            return user;
        }

        private void ArrangeSignup(User? existing)
        {
            _repositoryMock.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateTenantConfiguration());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(existing);
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _userMutationMock.Setup(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { ItemId = "u-new", IsSuccess = true });
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        }

        private static string Body(BaseAccountResponse r) => JsonSerializer.Serialize(r);

        private async Task<string> NewEmailSignupBodyAsync(SignupUserRequest request)
        {
            ArrangeSignup(null);
            return Body(await CreateService().SignupAccountAsync(request));
        }

        [Theory]
        [MemberData(nameof(AccountKinds))]
        public async Task Signup_EveryAccountState_SameBodyAsANewEmail(string kind)
        {
            var expected = await NewEmailSignupBodyAsync(new SignupUserRequest { Email = "user@example.com" });

            ArrangeSignup(AccountOf(kind));
            var result = await CreateService().SignupAccountAsync(new SignupUserRequest { Email = "user@example.com" });

            Body(result).Should().Be(expected);
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().BeNull("no user id is ever returned, so an existing account's id cannot leak");
            result.OrganizationId.Should().BeNull();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task Signup_ActiveAccount_SendsTheNormalPasswordResetMail_AndCreatesNothing()
        {
            var user = AccountOf("active")!;
            ArrangeSignup(user);

            await CreateService().SignupAccountAsync(new SignupUserRequest { Email = "user@example.com" });

            _iamServiceMock.Verify(s => s.SendEmailAsync(It.Is<SendMail>(m => m.Purpose == "RecoverAccount")), Times.Once);
            _cacheClientMock.Verify(c => c.AddStringValueAsync(AccountCodes.LatestKey(AccountCodes.Reset, user.ItemId), It.IsAny<string>(), It.IsAny<long>()), Times.Once,
                "the reset code is issued through AccountCodes, so it is the user's only live reset code");
            _iamServiceMock.Verify(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _userMutationMock.Verify(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()), Times.Never);
        }

        [Fact]
        public async Task Signup_PendingAccount_ResendsActivation()
        {
            var user = AccountOf("pending")!;
            ArrangeSignup(user);

            await CreateService().SignupAccountAsync(new SignupUserRequest { Email = "user@example.com" });

            _iamServiceMock.Verify(s => s.SendActivationToEmailAsync(user, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _cacheClientMock.Verify(c => c.AddStringValueAsync(AccountCodes.LatestKey(AccountCodes.Activation, user.ItemId), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
            _iamServiceMock.Verify(s => s.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
            _userMutationMock.Verify(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()), Times.Never);
        }

        [Theory]
        [InlineData("disabled")]
        [InlineData("suspended")]
        [InlineData("deactivated")]
        public async Task Signup_BlockedAccount_SendsNothing(string kind)
        {
            ArrangeSignup(AccountOf(kind));

            await CreateService().SignupAccountAsync(new SignupUserRequest { Email = "user@example.com" });

            _iamServiceMock.Verify(s => s.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
            _iamServiceMock.Verify(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _cacheClientMock.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
            _userMutationMock.Verify(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()), Times.Never);
        }

        [Theory]
        [InlineData("active")]
        [InlineData("pending")]
        public async Task Signup_MailFailsForAnExistingAccount_StillTheSameSuccess(string kind)
        {
            var expected = await NewEmailSignupBodyAsync(new SignupUserRequest { Email = "user@example.com" });
            ArrangeSignup(AccountOf(kind));
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>())).ThrowsAsync(new InvalidOperationException("queue down"));
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var result = await CreateService().SignupAccountAsync(new SignupUserRequest { Email = "user@example.com" });

            Body(result).Should().Be(expected);
        }

        [Theory]
        [InlineData("USER@Example.com")]
        [InlineData("  user@example.com  ")]
        public async Task Signup_CaseAndWhitespaceVariants_LookUpTheSameNormalizedEmail(string email)
        {
            ArrangeSignup(AccountOf("active"));

            var result = await CreateService().SignupAccountAsync(new SignupUserRequest { Email = email });

            result.IsSuccess.Should().BeTrue();
            _repositoryMock.Verify(r => r.GetUserByEmailAsync("user@example.com"), Times.Once);
            _userMutationMock.Verify(m => m.CreateUserAsync(It.IsAny<CreateUserRequest>()), Times.Never);
        }

        [Theory]
        [MemberData(nameof(AccountKinds))]
        public async Task Signup_OrganizationNameTaken_SameRefusalForEveryAccountState(string kind)
        {
            var taken = new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "name_already_exists", "Organization with same name already exists." } }
            };
            _resourceMutationMock.Setup(m => m.CheckOrganizationCreationAsync(It.IsAny<CreateOrganizationRequest>())).ReturnsAsync(taken);
            _organizationNameResolverMock.Setup(r => r.SuggestAvailableNamesAsync(It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(new List<string> { "Acme 2" });
            var request = new SignupUserRequest { Email = "user@example.com", CreateOrganizationDuringSignup = true, OrganizationName = "Acme" };

            var expected = await NewEmailSignupBodyAsync(request);
            ArrangeSignup(AccountOf(kind));
            var result = await CreateService().SignupAccountAsync(request);

            Body(result).Should().Be(expected);
            result.Errors.Should().ContainKey("name_already_exists");
            _resourceMutationMock.Verify(m => m.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string?>()), Times.Never);
            _iamServiceMock.Verify(s => s.SendEmailAsync(It.IsAny<SendMail>()), Times.Never);
        }

        // ---------- /auth/recover ----------

        private void ArrangeRecover(User? existing)
        {
            _recoverValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<RecoveryUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(existing);
            _repositoryMock.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
        }

        [Theory]
        [MemberData(nameof(AccountKinds))]
        public async Task Recover_MailQueueDown_SameSuccessForEveryAccountState(string kind)
        {
            ArrangeRecover(AccountOf(kind));
            _iamServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>())).ThrowsAsync(new InvalidOperationException("queue down"));
            _iamServiceMock.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("queue down"));

            var result = await CreateService().RecoverAccountAsync(new RecoveryUserRequest { Email = "user@example.com" });

            Body(result).Should().Be(Body(new BaseAccountResponse { IsSuccess = true }));
        }

        [Theory]
        [MemberData(nameof(AccountKinds))]
        public async Task Recover_LookupOrCodeStoreDown_SameSuccess(string kind)
        {
            ArrangeRecover(AccountOf(kind));
            _cacheClientMock.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            if (kind == "new")
            {
                _repositoryMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException("mongo down"));
            }

            var result = await CreateService().RecoverAccountAsync(new RecoveryUserRequest { Email = "user@example.com" });

            result.IsSuccess.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(AccountKinds))]
        public async Task Recover_ReadsTheIamConfiguration_ForEveryAccountState(string kind)
        {
            ArrangeRecover(AccountOf(kind));

            await CreateService().RecoverAccountAsync(new RecoveryUserRequest { Email = "user@example.com" });

            _repositoryMock.Verify(r => r.GetUserByEmailAsync(It.IsAny<string>()), Times.Once);
            _repositoryMock.Verify(r => r.GetIamConfigurationAsync(), Times.Once, "same reads with or without an account");
        }

        [Theory]
        [MemberData(nameof(AccountKinds))]
        public async Task Recover_CaptchaConfigured_NoCode_RefusedTheSameForEveryAccountState(string kind)
        {
            var captchaService = new Mock<ICaptchaService>();
            var captchaConfig = new Mock<ICaptchaConfigurationService>();
            captchaConfig.Setup(c => c.GetCaptchaConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, Provider = "recaptcha" });
            var realValidator = new RecoveryUserRequestValidator(captchaService.Object, captchaConfig.Object);
            ArrangeRecover(AccountOf(kind));
            _recoverValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<RecoveryUserRequest>(), It.IsAny<CancellationToken>()))
                .Returns((RecoveryUserRequest r, CancellationToken c) => realValidator.ValidateAsync(r, c));

            var result = await CreateService().RecoverAccountAsync(new RecoveryUserRequest { Email = "user@example.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { { "CaptchaCode", "Captcha verification is required." } });
            _repositoryMock.Verify(r => r.GetUserByEmailAsync(It.IsAny<string>()), Times.Never, "refused before any lookup");
            captchaService.Verify(s => s.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()), Times.Never);
        }
    }
}
