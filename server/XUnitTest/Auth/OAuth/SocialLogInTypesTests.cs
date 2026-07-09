using Authentication.DomainService.OAuth;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class SocialLogInTypesTests
    {
        [Theory]
        [InlineData(SocialLogInTypes.Google, "google")]
        [InlineData(SocialLogInTypes.FaceBook, "facebook")]
        [InlineData(SocialLogInTypes.LinkedIn, "linkedin")]
        [InlineData(SocialLogInTypes.Microsoft, "microsoft")]
        [InlineData(SocialLogInTypes.Apple, "apple")]
        [InlineData(SocialLogInTypes.Github, "github")]
        [InlineData(SocialLogInTypes.BYOSso, "byosso")]
        [InlineData(SocialLogInTypes.AzureAd, "azuread")]
        [InlineData(SocialLogInTypes.Okta, "okta")]
        [InlineData(SocialLogInTypes.Keycloak, "keycloak")]
        [InlineData(SocialLogInTypes.Ping, "ping")]
        [InlineData(SocialLogInTypes.Adfs, "adfs")]
        [InlineData(SocialLogInTypes.WindowsLive, "windowslive")]
        [InlineData(SocialLogInTypes.Auth0, "auth0")]
        [InlineData(SocialLogInTypes.Twitter, "x")]
        public void SocialLogInTypes_HaveExpectedValues(string actual, string expected)
        {
            actual.Should().Be(expected);
        }
    }
}