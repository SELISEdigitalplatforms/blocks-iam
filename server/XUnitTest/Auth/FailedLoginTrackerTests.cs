using Authentication.DomainService.Services;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Services;
using Moq;

namespace XUnitTest.Auth
{
    public class FailedLoginTrackerTests
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();

        public FailedLoginTrackerTests()
        {
            _repo.Setup(r => r.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { GetNumberOfWrongAttemptsToLockTheAccount = 4, AccountLockDurationInMinutes = 9 });
        }

        [Fact]
        public async Task NoFreeSlot_IsLocked_AndThePasswordIsNotChecked()
        {
            var checkedPassword = false;

            var result = await new FailedLoginTracker(_repo.Object).CheckPasswordAsync("u1", () => checkedPassword = true);

            result.Result.Should().Be(GuardedAttemptResult.Locked);
            checkedPassword.Should().BeFalse();
        }

        [Fact]
        public async Task RightPassword_GivesTheSlotBack()
        {
            _repo.Setup(r => r.TryReserveLoginAttemptAsync("u1", 4, It.IsAny<DateTime>())).ReturnsAsync(new User { ItemId = "u1", FailedLoginCount = 1 });
            _repo.Setup(r => r.ReleaseLoginAttemptAsync("u1", It.IsAny<DateTime>())).ReturnsAsync(true);

            var result = await new FailedLoginTracker(_repo.Object).CheckPasswordAsync("u1", () => true);

            result.IsValid.Should().BeTrue();
            _repo.Verify(r => r.ReleaseLoginAttemptAsync("u1", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task WrongPassword_ThatTookTheLastSlot_Locks_WithTheTenantSettings()
        {
            var locked = new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(9) };
            _repo.Setup(r => r.TryReserveLoginAttemptAsync("u1", 4, It.IsAny<DateTime>())).ReturnsAsync(new User { ItemId = "u1", FailedLoginCount = 4 });
            _repo.Setup(r => r.LockAccountIfLoginThresholdReachedAsync("u1", 4, 9, It.IsAny<DateTime>())).ReturnsAsync(locked);

            var result = await new FailedLoginTracker(_repo.Object).CheckPasswordAsync("u1", () => false);

            result.Result.Should().Be(GuardedAttemptResult.Invalid);
            result.LockedNow.Should().BeSameAs(locked);
            result.IsLocked.Should().BeTrue();
            _repo.Verify(r => r.ReleaseLoginAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }
    }
}
