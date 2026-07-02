using Authentication.DomainService.Utilities;
using FluentAssertions;

namespace XUnitTest.Auth.Shared
{
    public class SessionTimeoutConfigTests
    {
        [Fact]
        public void GetIdleTimeout_DefaultIs24Hours()
        {
            var prevValue = Environment.GetEnvironmentVariable("IDP_SESSION_IDLE_HOURS");
            try
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_IDLE_HOURS", null);
                var timeout = SessionTimeoutConfig.GetIdleTimeout();
                timeout.Should().Be(TimeSpan.FromHours(24));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_IDLE_HOURS", prevValue);
            }
        }

        [Fact]
        public void GetIdleTimeout_RespectsEnvironmentVariable()
        {
            var prevValue = Environment.GetEnvironmentVariable("IDP_SESSION_IDLE_HOURS");
            try
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_IDLE_HOURS", "48");
                var timeout = SessionTimeoutConfig.GetIdleTimeout();
                timeout.Should().Be(TimeSpan.FromHours(48));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_IDLE_HOURS", prevValue);
            }
        }

        [Theory]
        [InlineData("-5")]
        [InlineData("0")]
        [InlineData("invalid")]
        [InlineData("200")]
        public void GetIdleTimeout_FallsBackToDefault_OnInvalidValue(string value)
        {
            var prevValue = Environment.GetEnvironmentVariable("IDP_SESSION_IDLE_HOURS");
            try
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_IDLE_HOURS", value);
                var timeout = SessionTimeoutConfig.GetIdleTimeout();
                timeout.Should().Be(TimeSpan.FromHours(24));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_IDLE_HOURS", prevValue);
            }
        }

        [Fact]
        public void GetAbsoluteTimeoutHours_DefaultIs5Hours()
        {
            var prevValue = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS");
            try
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS", null);
                var timeout = SessionTimeoutConfig.GetAbsoluteTimeoutHours();
                timeout.Should().Be(TimeSpan.FromHours(5));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS", prevValue);
            }
        }

        [Fact]
        public void GetAbsoluteTimeoutHours_RespectsEnvironmentVariable()
        {
            var prevValue = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS");
            try
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS", "72");
                var timeout = SessionTimeoutConfig.GetAbsoluteTimeoutHours();
                timeout.Should().Be(TimeSpan.FromHours(72));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS", prevValue);
            }
        }

        [Fact]
        public void GetAbsoluteTimeoutDays_DefaultIs30Days()
        {
            var prevValue = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS");
            try
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS", null);
                var timeout = SessionTimeoutConfig.GetAbsoluteTimeoutDays();
                timeout.Should().Be(TimeSpan.FromDays(30));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS", prevValue);
            }
        }

        [Fact]
        public void GetAbsoluteTimeoutDays_RespectsEnvironmentVariable()
        {
            var prevValue = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS");
            try
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS", "7");
                var timeout = SessionTimeoutConfig.GetAbsoluteTimeoutDays();
                timeout.Should().Be(TimeSpan.FromDays(7));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS", prevValue);
            }
        }
    }
}