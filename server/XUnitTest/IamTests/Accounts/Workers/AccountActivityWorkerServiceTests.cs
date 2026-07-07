using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.IamTests.Accounts.TestHelpers;

namespace XUnitTest.IamTests.Accounts.Workers
{
    public class AccountActivityWorkerServiceTests : IDisposable
    {
        private readonly Mock<ILogger<AccountActivityWorkerService>> _loggerMock = new();
        private readonly Mock<IIdentityAccessManagementRepository> _repositoryMock = new();
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock = new();
        private readonly Mock<ICacheClient> _cacheClientMock = new();

        public AccountActivityWorkerServiceTests()
        {
            TestDataBuilder.InstallBlocksContext();
        }

        public void Dispose()
        {
            TestDataBuilder.ResetBlocksContext();
        }

        private AccountActivityWorkerService CreateWorker() =>
            new(
                _loggerMock.Object,
                _repositoryMock.Object,
                _iamServiceMock.Object,
                _cacheClientMock.Object);

        [Fact]
        public async Task Consume_WithBlankCode_SkipsCacheInvalidation_StillPersistsTimeline()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: string.Empty, userId: user.ItemId);

            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            await worker.Consume(evt);

            _repositoryMock.Verify(r => r.UpdateUserKeyMapActivationAsync(It.IsAny<string>()), Times.Never);
            _cacheClientMock.Verify(c => c.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
            _repositoryMock.Verify(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>()), Times.Once);
        }

        [Fact]
        public async Task Consume_WithCode_RemovesCachedKeys_AndUpdatesKeyMapActivation()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var maps = TestDataBuilder.CreateUserKeyMaps(user.ItemId, 2);
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-abc", userId: user.ItemId);

            _repositoryMock.Setup(r => r.GetActiveUserKeyMapAsync(user.ItemId)).ReturnsAsync(maps);
            _repositoryMock.Setup(r => r.UpdateUserKeyMapActivationAsync(user.ItemId)).ReturnsAsync(true);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);
            _cacheClientMock.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);

            await worker.Consume(evt);

            foreach (var map in maps)
            {
                _cacheClientMock.Verify(c => c.RemoveKeyAsync(map.Key), Times.Once);
            }
            _repositoryMock.Verify(r => r.UpdateUserKeyMapActivationAsync(user.ItemId), Times.Once);
        }

        [Fact]
        public async Task Consume_ActivateAccount_NotPrevented_SendsActivationEmail()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-1", userId: user.ItemId, @event: "Activate_Account", preventPostEvent: false, mailPurpose: "AccountActivated");

            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);
            _iamServiceMock.Setup(s => s.SendAccountActivationEmailAsync(user, "AccountActivated")).ReturnsAsync(true);

            await worker.Consume(evt);

            _iamServiceMock.Verify(s => s.SendAccountActivationEmailAsync(user, "AccountActivated"), Times.Once);
            _iamServiceMock.Verify(s => s.SendToQueueAsync(It.IsAny<string>(), It.IsAny<LogoutAllEvent>()), Times.Never);
        }

        [Fact]
        public async Task Consume_ActivateAccount_Prevented_SkipsPostEvent()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-1", userId: user.ItemId, @event: "Activate_Account", preventPostEvent: true);

            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            await worker.Consume(evt);

            _iamServiceMock.Verify(s => s.SendAccountActivationEmailAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
            _iamServiceMock.Verify(s => s.SendToQueueAsync(It.IsAny<string>(), It.IsAny<LogoutAllEvent>()), Times.Never);
        }

        [Fact]
        public async Task Consume_ResetPassword_SendsLogoutAllEvent()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-1", userId: user.ItemId, @event: "Reset_Password", preventPostEvent: false);

            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            await worker.Consume(evt);

            _iamServiceMock.Verify(s => s.SendToQueueAsync(
                IdpConstants.AuthenticationQueue,
                It.Is<LogoutAllEvent>(l => l.UserId == user.ItemId)), Times.Once);
        }

        [Fact]
        public async Task Consume_ResetPassword_Prevented_DoesNotSendLogoutAll()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-1", userId: user.ItemId, @event: "Reset_Password", preventPostEvent: true);

            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            await worker.Consume(evt);

            _iamServiceMock.Verify(s => s.SendToQueueAsync(It.IsAny<string>(), It.IsAny<LogoutAllEvent>()), Times.Never);
        }

        [Fact]
        public async Task Consume_UnknownEvent_DoesNotThrow_AndPersistsTimeline()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-1", userId: user.ItemId, @event: "Login_Attempt");

            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>())).ReturnsAsync(true);

            var act = async () => await worker.Consume(evt);
            await act.Should().NotThrowAsync();

            _repositoryMock.Verify(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>()), Times.Once);
        }

        [Fact]
        public async Task Consume_NullContext_Throws()
        {
            var worker = CreateWorker();
            var act = async () => await worker.Consume(null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task SaveUserTimeline_SetsOrganizationAndCreatedByFromBlocksContext()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-x", userId: user.ItemId, @event: "Activate_Account");

            UserTimeline? captured = null;
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>()))
                .Callback<UserTimeline>(t => captured = t)
                .ReturnsAsync(true);

            await worker.SaveUserTimeline(user, evt);

            captured.Should().NotBeNull();
            captured!.UserId.Should().Be(user.ItemId);
            captured.Event.Should().Be("Activate_Account");
            captured.CreatedBy.Should().Be(TestDataBuilder.DefaultUserId);
            captured.OrganizationId.Should().Be(TestDataBuilder.DefaultOrganizationId);
            captured.ItemId.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task SaveUserTimeline_WhenContextUserIdBlank_UsesUserCreatedBy()
        {
            var worker = CreateWorker();
            TestDataBuilder.ResetBlocksContext();
            TestDataBuilder.InstallBlocksContext(userId: string.Empty);

            var user = TestDataBuilder.CreateUser();
            var evt = TestDataBuilder.CreateAccountActivityEvent(code: "code-x", userId: user.ItemId, @event: "Activate_Account");

            UserTimeline? captured = null;
            _repositoryMock.Setup(r => r.InsertUserTimelineAsync(It.IsAny<UserTimeline>()))
                .Callback<UserTimeline>(t => captured = t)
                .ReturnsAsync(true);

            await worker.SaveUserTimeline(user, evt);

            captured.Should().NotBeNull();
            captured!.CreatedBy.Should().Be(user.CreatedBy);
        }

        [Fact]
        public async Task HandlePostEventForActivation_DelegatesToService()
        {
            var worker = CreateWorker();
            var user = TestDataBuilder.CreateUser();
            _iamServiceMock.Setup(s => s.SendAccountActivationEmailAsync(user, "AccountActivated")).ReturnsAsync(true);

            var result = await worker.HandlePostEventForActivation(user, "AccountActivated");

            result.Should().BeTrue();
            _iamServiceMock.Verify(s => s.SendAccountActivationEmailAsync(user, "AccountActivated"), Times.Once);
        }

        [Fact]
        public async Task HandlePostEventForResetPassword_PublishesLogoutAll_AndReturnsTrue()
        {
            var worker = CreateWorker();

            var result = await worker.HandlePostEventForResetPassword("u-42");

            result.Should().BeTrue();
            _iamServiceMock.Verify(s => s.SendToQueueAsync(
                IdpConstants.AuthenticationQueue,
                It.Is<LogoutAllEvent>(l => l.UserId == "u-42")), Times.Once);
        }
    }
}
