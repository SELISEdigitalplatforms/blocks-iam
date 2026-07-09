using Authentication.DomainService.OAuth;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class SocialCallbackResultTests
    {
        [Fact]
        public void DefaultExternalUserData_IsBYOSsoUserData()
        {
            var result = new SocialCallbackResult();
            result.ExternalUserData.Should().BeOfType<BYOSsoUserData>();
        }

        [Fact]
        public void AccessTokenDefaultsToNull()
        {
            new SocialCallbackResult().AccessToken.Should().BeNull();
        }

        [Fact]
        public void IdTokenDefaultsToNull()
        {
            new SocialCallbackResult().IdToken.Should().BeNull();
        }

        [Fact]
        public void RefreshTokenDefaultsToNull()
        {
            new SocialCallbackResult().RefreshToken.Should().BeNull();
        }

        [Fact]
        public void CanSetTokensAndExternalUser()
        {
            var userData = new GoogleUserData { Email = "u@e.com" };
            var result = new SocialCallbackResult
            {
                ExternalUserData = userData,
                AccessToken = "at",
                IdToken = "it",
                RefreshToken = "rt"
            };

            result.ExternalUserData.Should().BeSameAs(userData);
            result.AccessToken.Should().Be("at");
            result.IdToken.Should().Be("it");
            result.RefreshToken.Should().Be("rt");
        }
    }
}