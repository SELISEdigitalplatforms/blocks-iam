using Authentication.DomainService.Services;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Moq;

namespace XUnitTest.Auth
{
    public class FailedLoginTrackerTests
    {
        [Fact]
        public async Task RecordFailedAttempt_UsesLoginThresholdAndDurationFromConfiguration()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { GetNumberOfWrongAttemptsToLockTheAccount = 4, AccountLockDurationInMinutes = 9 });
            var user = new User { ItemId = "u1" };
            repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync("u1", 4, 9, It.IsAny<DateTime>())).ReturnsAsync(user);

            var result = await new FailedLoginTracker(repo.Object).RecordFailedAttemptAsync("u1");

            result.Should().BeSameAs(user);
        }

        [Fact]
        public async Task RecordFailedAttempt_NoConfiguration_ReturnsNull_AndDoesNotCount()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

            var result = await new FailedLoginTracker(repo.Object).RecordFailedAttemptAsync("u1");

            result.Should().BeNull();
            repo.Verify(r => r.IncrementFailedLoginAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
        }
    }
}
