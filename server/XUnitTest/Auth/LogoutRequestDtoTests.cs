using Authentication.DomainService.Authentication;
using FluentAssertions;

namespace XUnitTest.Auth
{
    public class LogoutRequestDtoTests
    {
        [Fact]
        public void LogoutRequest_DefaultValues_AreSensible()
        {
            var req = new LogoutRequest();
            req.RefreshToken.Should().BeNull();
        }

        [Fact]
        public void LogoutAllRequest_DefaultUseBackchannel_IsFalse()
        {
            new LogoutAllRequest().UseBackchannel.Should().BeFalse();
        }

        [Fact]
        public void LogoutAllRequest_CanSetBackchannel()
        {
            var req = new LogoutAllRequest { UseBackchannel = true };
            req.UseBackchannel.Should().BeTrue();
        }

        [Fact]
        public void LogoutResponse_DefaultIsSuccess_IsFalse()
        {
            new LogoutResponse().IsSuccess.Should().BeFalse();
        }

        [Fact]
        public void LogoutResponse_CanSetIsSuccess()
        {
            new LogoutResponse { IsSuccess = true }.IsSuccess.Should().BeTrue();
        }
    }
}