using Authentication.DomainService.Services;
using FluentAssertions;

namespace XUnitTest.Auth
{
    public class LockoutDurationTests
    {
        [Fact]
        public void TenantBaseLongerThanTheCap_IsNeverShortened()
        {
            AuthenticationRepository.LockoutDurationMinutes(previousLocks: 3, baseDurationMinutes: 300).Should().Be(300);
        }
    }
}
