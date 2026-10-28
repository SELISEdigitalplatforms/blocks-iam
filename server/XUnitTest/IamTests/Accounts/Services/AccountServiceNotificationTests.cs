using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Notifications;
using Iam.DomainService.Shared.Entities;
using Moq;
using XUnitTest.IamTests.Accounts.TestHelpers;
using XUnitTest.IamTests.Notifications;

namespace XUnitTest.IamTests.Accounts.Services
{
    public partial class AccountServiceTests
    {
        private (AccountService Service, RecordingSender Sender) CreateNotifyingService()
        {
            var sender = new RecordingSender();
            var service = new AccountService(
                _loggerMock.Object, _repositoryMock.Object, _iamServiceMock.Object, _cacheClientMock.Object, _tenantsMock.Object,
                _accountValidatorMock.Object, _changePasswordValidatorMock.Object, _recoverValidatorMock.Object,
                _userMutationMock.Object, _resourceMutationMock.Object, _captchaServiceMock.Object, _captchaConfigurationServiceMock.Object,
                _userActivityDispatcherMock.Object, new SignupOrganizationResolver(_resourceRepositoryMock.Object),
                notificationSender: sender);
            return (service, sender);
        }

        [Fact]
        public async Task Locked_NotifiesThatUser_WithContextOrganization_AndNoEmailNeeded()
        {
            TestContext.Install(orgId: "org-1");
            try
            {
                var (service, sender) = CreateNotifyingService();
                var user = TestDataBuilder.CreateUser();
                user.Email = null;
                user.OrganizationIds = new List<string> { "org-1" };

                await service.SendAccountLockedNotificationAsync(user, DateTime.UtcNow.AddMinutes(15));

                var sent = sender.Sent.Should().ContainSingle().Subject;
                sent.ConfigurationName.Should().Be("iam-account-locked");
                sent.UserIds.Should().Equal(user.ItemId);
                sent.TenantId.Should().Be("tenant-1");
                sent.OrganizationId.Should().Be("org-1");
                var json = RecordingSender.PayloadJson(sent);
                json.Should().Contain("lockoutUntilUtc").And.Contain("too_many_failed_attempts");
            }
            finally
            {
                TestContext.Clear();
            }
        }

        [Fact]
        public async Task Locked_WithoutContextOrganization_SendsOneNoticePerAccountOrganization()
        {
            TestContext.Install(orgId: "");
            try
            {
                var (service, sender) = CreateNotifyingService();
                var user = TestDataBuilder.CreateUser();
                user.OrganizationIds = new List<string> { "org-1", "org-2", "org-1" };

                await service.SendAccountLockedNotificationAsync(user, DateTime.UtcNow.AddMinutes(15));

                sender.Sent.Select(n => n.OrganizationId).Should().Equal("org-1", "org-2");
            }
            finally
            {
                TestContext.Clear();
            }
        }

        [Fact]
        public async Task Unlock_ByAdmin_NotifiesThatUser_AndNotifierFailureDoesNotFailUnlock()
        {
            TestContext.Install(orgId: "org-1");
            try
            {
                var (service, sender) = CreateNotifyingService();
                sender.Throw = true;
                var user = TestDataBuilder.CreateUser();
                user.OrganizationIds = new List<string> { "org-1" };
                _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
                _repositoryMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

                var result = await service.UnlockAccountAsync(user.ItemId);

                result.IsSuccess.Should().BeTrue();
                var sent = sender.Sent.Should().ContainSingle().Subject;
                sent.ConfigurationName.Should().Be("iam-account-unlocked");
                sent.UserIds.Should().Equal(user.ItemId);
                sent.OrganizationId.Should().Be("org-1");
            }
            finally
            {
                TestContext.Clear();
            }
        }

        [Fact]
        public async Task Unlock_UserNotFound_NoNotification()
        {
            var (service, sender) = CreateNotifyingService();
            _repositoryMock.Setup(r => r.GetUserByIdAsync("missing")).ReturnsAsync((User)null!);

            var result = await service.UnlockAccountAsync("missing");

            result.IsSuccess.Should().BeFalse();
            sender.Sent.Should().BeEmpty();
        }
    }
}
