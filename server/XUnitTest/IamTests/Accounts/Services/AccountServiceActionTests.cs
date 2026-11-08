using Blocks.Genesis;
using FluentAssertions;
using FluentValidation.Results;
using Iam.DomainService.Accounts;
using Iam.DomainService.Actions;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Moq;
using XUnitTest.IamTests.Accounts.TestHelpers;
using System.Text.Json;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.IamTests.Accounts.Services
{
    /// <summary>
    /// Password, lock and unlock changes reach the tenant's webhooks only after they were saved,
    /// and only as ids and flags.
    /// </summary>
    public partial class AccountServiceTests
    {
        private AccountService ServiceWithActions(List<(string Action, string Json)> published, IFailedLoginTracker? tracker = null)
        {
            var actions = new Mock<IActionPublisher>();
            actions.Setup(a => a.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?, string?, CancellationToken>((name, data, _, _) => published.Add((name, JsonSerializer.Serialize(data))))
                .Returns(Task.CompletedTask);

            return new AccountService(_loggerMock.Object, _repositoryMock.Object, _iamServiceMock.Object, _cacheClientMock.Object, _tenantsMock.Object,
                _accountValidatorMock.Object, _changePasswordValidatorMock.Object, _recoverValidatorMock.Object,
                _userMutationMock.Object, _resourceMutationMock.Object, _captchaServiceMock.Object, _captchaConfigurationServiceMock.Object,
                _userActivityDispatcherMock.Object, new SignupOrganizationResolver(_resourceRepositoryMock.Object),
                failedLoginTracker: tracker, actionPublisher: actions.Object);
        }

        [Fact]
        public async Task EmailSignup_MarksTheCreateRequestAsSelfSignup()
        {
            ArrangeNewEmailSignup(MultiOrgConfig(autoJoin: false), null);

            await CreateService().SignupAccountAsync(new Iam.DomainService.Accounts.SignupUserRequest { Email = "a@x.io" });

            _userMutationMock.Verify(m => m.CreateSignupUserWithoutOrganizationAsync(It.Is<CreateUserRequest>(r => r.IsSelfSignup)), Times.Once);
        }

        [Fact]
        public async Task ChangePassword_PublishesPasswordChangedViaSelf_AfterTheSave()
        {
            var published = new List<(string Action, string Json)>();
            var user = SetUpChangePasswordUser();

            var result = await ServiceWithActions(published).ChangePasswordAsync(RightOld());

            result.IsSuccess.Should().BeTrue();
            published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserPasswordChanged, $"{{\"userId\":\"{user.ItemId}\",\"via\":\"self\"}}"));
        }

        [Fact]
        public async Task ChangePassword_WithWrongOldPassword_PublishesNothing()
        {
            var published = new List<(string Action, string Json)>();
            SetUpChangePasswordUser();

            var result = await ServiceWithActions(published).ChangePasswordAsync(WrongOld());

            result.IsSuccess.Should().BeFalse();
            published.Should().BeEmpty();
        }

        [Fact]
        public async Task ChangePassword_WhenTheWrongOldPasswordLocksTheAccount_PublishesLockedOnce()
        {
            var published = new List<(string Action, string Json)>();
            var user = SetUpChangePasswordUser();
            var service = ServiceWithActions(published, new FakeFailedLoginTracker(user, 2));

            await service.ChangePasswordAsync(WrongOld());
            published.Should().BeEmpty();
            await service.ChangePasswordAsync(WrongOld());
            await service.ChangePasswordAsync(WrongOld());

            published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserLocked, $"{{\"userId\":\"{user.ItemId}\",\"reason\":\"failed_logins\"}}"));
        }

        [Fact]
        public async Task ResetPassword_PublishesPasswordChangedViaResetLink_AfterTheSave()
        {
            var published = new List<(string Action, string Json)>();
            var user = SetupValidReset();

            var result = await ServiceWithActions(published).ResetAccountPasswordAsync(new ResetPasswordRequest { Code = "x", Password = "NewP@ssw0rd" });

            result.IsSuccess.Should().BeTrue();
            published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserPasswordChanged, $"{{\"userId\":\"{user.ItemId}\",\"via\":\"reset_link\"}}"));
        }

        [Fact]
        public async Task ResetPassword_WhenTheSaveFails_PublishesNothing()
        {
            var published = new List<(string Action, string Json)>();
            SetupValidReset();
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            var result = await ServiceWithActions(published).ResetAccountPasswordAsync(new ResetPasswordRequest { Code = "x", Password = "NewP@ssw0rd" });

            result.IsSuccess.Should().BeFalse();
            published.Should().BeEmpty();
        }

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 0)]
        public async Task Unlock_PublishesUnlockedByAdmin_OnlyWhenTheSaveWorked(bool saved, int count)
        {
            var published = new List<(string Action, string Json)>();
            var user = TestDataBuilder.CreateUser();
            user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(saved);

            await ServiceWithActions(published).UnlockAccountAsync(user.ItemId);

            published.Should().HaveCount(count);
            if (saved)
            {
                published[0].Should().Be((IamActionNames.UserUnlocked, $"{{\"userId\":\"{user.ItemId}\",\"by\":\"admin\"}}"));
            }
        }
    }
}
