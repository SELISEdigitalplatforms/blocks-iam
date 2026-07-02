using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
using FluentAssertions;

namespace XUnitTest.Auth.Shared
{
    public class AuthenticationConstantsTests
    {
        [Fact]
        public void SeverityConstants_HaveExpectedValues()
        {
            AuthenticationConstants.SeverityInfo.Should().Be("INFO");
            AuthenticationConstants.SeverityWarn.Should().Be("WARN");
            AuthenticationConstants.SeverityError.Should().Be("ERROR");
            AuthenticationConstants.SeverityCritical.Should().Be("CRITICAL");
        }

        [Fact]
        public void StatusConstants_HaveExpectedValues()
        {
            AuthenticationConstants.StatusSuccess.Should().Be("success");
            AuthenticationConstants.StatusFailure.Should().Be("failure");
            AuthenticationConstants.StatusSent.Should().Be("sent");
            AuthenticationConstants.StatusDelivered.Should().Be("delivered");
        }

        [Fact]
        public void PkceAndScopeConstants_AreCorrect()
        {
            AuthenticationConstants.PkceMethodS256.Should().Be("S256");
            AuthenticationConstants.OpenIdProfileEmailScope.Should().Be("openid profile email");
            AuthenticationConstants.DefaultOrganizationId.Should().Be("default");
        }

        [Fact]
        public void SessionTimeouts_HaveExpectedValues()
        {
            AuthenticationConstants.MaxIdpSessionHours.Should().Be(168);
            AuthenticationConstants.DefaultIdpSessionIdleHours.Should().Be(24);
            AuthenticationConstants.DefaultIdpSessionAbsoluteHours.Should().Be(5);
        }

        [Fact]
        public void BackchannelConstants_HaveExpectedValues()
        {
            AuthenticationConstants.BackchannelRetryBackoffMilliseconds.Should().Be(250);
            AuthenticationConstants.BackchannelLogoutMaxAttempts.Should().Be(3);
            AuthenticationConstants.BackchannelTimeoutSeconds.Should().Be(100);
        }

        [Fact]
        public void CacheTtls_HaveExpectedValues()
        {
            AuthenticationConstants.SocialAuthorizationUrlCacheTtlSeconds.Should().Be(300);
            AuthenticationConstants.OidcAuthorizationCodeCacheTtlSeconds.Should().Be(600);
            AuthenticationConstants.OidcStateCacheTtlSeconds.Should().Be(300);
            AuthenticationConstants.IdpFlowCacheTtlSeconds.Should().Be(600);
        }

        [Fact]
        public void TokenLifetimeConstants_HaveExpectedValues()
        {
            AuthenticationConstants.MinAccessTokenLifetimeSeconds.Should().Be(60);
            AuthenticationConstants.SecondsPerMinute.Should().Be(60);
            AuthenticationConstants.MinTokenLifetimeMinutes.Should().Be(1);
        }

        [Fact]
        public void UriConstants_HaveExpectedValues()
        {
            AuthenticationConstants.AppleAuthUrl.Should().Be("https://appleid.apple.com");
            AuthenticationConstants.GithubUserEmailsUrl.Should().Be("https://api.github.com/user/emails");
            AuthenticationConstants.FallbackIssuer.Should().Be("https://localhost:5000");
            AuthenticationConstants.ProtectedApiAudience.Should().Be("api://blocks-protected-api");
            AuthenticationConstants.LocalhostDefaultUrl.Should().Be("https://localhost:5000");
        }
    }
}