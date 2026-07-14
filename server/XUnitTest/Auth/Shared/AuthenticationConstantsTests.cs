using Iam.DomainService.Utilities;
using FluentAssertions;

namespace XUnitTest.Auth.Shared
{
    public class AuthenticationConstantsTests
    {
        [Fact]
        public void SeverityConstants_HaveExpectedValues()
        {
            IdpConstants.SeverityInfo.Should().Be("INFO");
            IdpConstants.SeverityWarn.Should().Be("WARN");
            IdpConstants.SeverityError.Should().Be("ERROR");
            IdpConstants.SeverityCritical.Should().Be("CRITICAL");
        }

        [Fact]
        public void StatusConstants_HaveExpectedValues()
        {
            IdpConstants.StatusSuccess.Should().Be("success");
            IdpConstants.StatusFailure.Should().Be("failure");
            IdpConstants.StatusSent.Should().Be("sent");
            IdpConstants.StatusDelivered.Should().Be("delivered");
        }

        [Fact]
        public void PkceAndScopeConstants_AreCorrect()
        {
            IdpConstants.PkceMethodS256.Should().Be("S256");
            IdpConstants.OpenIdProfileEmailScope.Should().Be("openid profile email");
            IdpConstants.DefaultOrganizationId.Should().Be("default");
        }

        [Fact]
        public void SessionTimeouts_HaveExpectedValues()
        {
            IdpConstants.MaxIdpSessionHours.Should().Be(168);
            IdpConstants.DefaultIdpSessionIdleHours.Should().Be(24);
            IdpConstants.DefaultIdpSessionAbsoluteHours.Should().Be(5);
        }

        [Fact]
        public void BackchannelConstants_HaveExpectedValues()
        {
            IdpConstants.BackchannelRetryBackoffMilliseconds.Should().Be(250);
            IdpConstants.BackchannelLogoutMaxAttempts.Should().Be(3);
            IdpConstants.BackchannelTimeoutSeconds.Should().Be(100);
        }

        [Fact]
        public void CacheTtls_HaveExpectedValues()
        {
            IdpConstants.SocialAuthorizationUrlCacheTtlSeconds.Should().Be(300);
            IdpConstants.OidcAuthorizationCodeCacheTtlSeconds.Should().Be(600);
            IdpConstants.OidcStateCacheTtlSeconds.Should().Be(300);
            IdpConstants.IdpFlowCacheTtlSeconds.Should().Be(600);
        }

        [Fact]
        public void TokenLifetimeConstants_HaveExpectedValues()
        {
            IdpConstants.MinAccessTokenLifetimeSeconds.Should().Be(60);
            IdpConstants.SecondsPerMinute.Should().Be(60);
            IdpConstants.MinTokenLifetimeMinutes.Should().Be(1);
        }

        [Fact]
        public void UriConstants_HaveExpectedValues()
        {
            IdpConstants.AppleAuthUrl.Should().Be("https://appleid.apple.com");
            IdpConstants.GithubUserEmailsUrl.Should().Be("https://api.github.com/user/emails");
            IdpConstants.FallbackIssuer.Should().Be("https://localhost:5000");
            IdpConstants.ProtectedApiAudience.Should().Be("api://blocks-protected-api");
            IdpConstants.LocalhostDefaultUrl.Should().Be("https://localhost:5000");
        }
    }
}