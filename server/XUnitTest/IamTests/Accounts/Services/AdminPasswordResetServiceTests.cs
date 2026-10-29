using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using XUnitTest.IamTests.Accounts.TestHelpers;

namespace XUnitTest.IamTests.Accounts.Services
{
    /// <summary>
    /// The admin password reset: no captcha, honest answers, one mail per user per minute,
    /// and the slot is given back when the mail could not be queued.
    /// </summary>
    public class AdminPasswordResetServiceTests
    {
        private const string UserId = "user-1";
        private const string ResetPurpose = "RecoverAccount";

        private readonly Mock<IUserRepository> _users = new();
        private readonly Mock<IAccountService> _accounts = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IDatabase> _db = new();
        private readonly Mock<IAccessGrantService> _accessGrant = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly HashSet<string> _heldSlots = new();
        private readonly List<UserActivityEvent> _events = new();

        public AdminPasswordResetServiceTests()
        {
            _cache.Setup(c => c.CacheDatabase()).Returns(_db.Object);
            _db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(), When.NotExists, It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey k, RedisValue _, TimeSpan? _, bool _, When _, CommandFlags _) => _heldSlots.Add(k.ToString()));
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>()))
                .Callback<string>(k => _heldSlots.Remove(k))
                .ReturnsAsync(true);
            _accessGrant.Setup(a => a.AuthorizeCurrentCallerAsync(It.IsAny<Func<GrantScope, GrantDenial?>>()))
                .ReturnsAsync(true);
            _accounts.Setup(a => a.ProcessRecoverAccountAsync(It.IsAny<User>(), ResetPurpose, null, null))
                .ReturnsAsync(true);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => _events.Add(e))
                .Returns(Task.CompletedTask);
        }

        private AdminPasswordResetService Sut() => new(
            _users.Object, _accounts.Object, _cache.Object, _accessGrant.Object, _activity.Object,
            new Mock<ILogger<AdminPasswordResetService>>().Object);

        private User GivenUser(bool active = true, UserLifecycleStatus status = UserLifecycleStatus.Active)
        {
            var user = TestDataBuilder.CreateUser(UserId, active: active);
            user.Status = status;
            _users.Setup(r => r.GetUserByIdAsync(UserId)).ReturnsAsync(user);
            return user;
        }

        private static AdminPasswordResetRequest Request() => new() { UserId = UserId };

        private void MailNeverSent() =>
            _accounts.Verify(a => a.ProcessRecoverAccountAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);

        [Fact]
        public async Task ActiveUser_GetsResetMail_AndTheResetIsRecorded()
        {
            var user = GivenUser();

            var result = await Sut().SendResetMailAsync(Request());

            result.IsSuccess.Should().BeTrue();
            _accounts.Verify(a => a.ProcessRecoverAccountAsync(user, ResetPurpose, null, null), Times.Once);
            _events.Should().ContainSingle(e => e.UserId == UserId && e.Event == "PASSWORD_RESET_SENT_BY_ADMIN");
        }

        [Fact]
        public async Task UnknownUser_IsRefused_WithoutMail()
        {
            var result = await Sut().SendResetMailAsync(Request());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(nameof(AdminPasswordResetRequest.UserId));
            MailNeverSent();
        }

        [Fact]
        public async Task BlockedUser_IsRefused_WithoutMail()
        {
            GivenUser(status: UserLifecycleStatus.Suspended);

            var result = await Sut().SendResetMailAsync(Request());

            result.IsSuccess.Should().BeFalse();
            MailNeverSent();
        }

        [Fact]
        public async Task NotActivatedUser_IsRefused_WithoutMail()
        {
            GivenUser(active: false, status: UserLifecycleStatus.PendingVerification);

            var result = await Sut().SendResetMailAsync(Request());

            result.IsSuccess.Should().BeFalse();
            MailNeverSent();
        }

        [Fact]
        public async Task CallerWhoMayNotManageTheUser_GetsGrantError_WithoutMail()
        {
            GivenUser();
            _accessGrant.Setup(a => a.AuthorizeCurrentCallerAsync(It.IsAny<Func<GrantScope, GrantDenial?>>()))
                .ReturnsAsync(false);

            var result = await Sut().SendResetMailAsync(Request());

            AccessGrantErrors.IsGrantNotAllowed(result.Errors).Should().BeTrue();
            MailNeverSent();
        }

        [Fact]
        public async Task SecondResetWithinAMinute_IsRefused_AndSendsOneMail()
        {
            GivenUser();
            var sut = Sut();

            (await sut.SendResetMailAsync(Request())).IsSuccess.Should().BeTrue();
            var second = await sut.SendResetMailAsync(Request());

            second.IsSuccess.Should().BeFalse();
            second.Errors.Should().ContainKey(AdminPasswordResetService.ResetMail);
            _accounts.Verify(a => a.ProcessRecoverAccountAsync(It.IsAny<User>(), ResetPurpose, null, null), Times.Once);
        }

        [Fact]
        public async Task MailNotQueued_IsReported_AndTheAdminMayRetryAtOnce()
        {
            GivenUser();
            _accounts.Setup(a => a.ProcessRecoverAccountAsync(It.IsAny<User>(), ResetPurpose, null, null))
                .ReturnsAsync(false);

            var result = await Sut().SendResetMailAsync(Request());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(AdminPasswordResetService.ResetMail);
            _heldSlots.Should().NotContain(AdminPasswordResetService.WaitKey(UserId));
            _events.Should().BeEmpty();
        }
    }
}
