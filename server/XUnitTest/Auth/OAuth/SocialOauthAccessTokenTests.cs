using Authentication.DomainService.OAuth;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class SocialOauthAccessTokenTests
    {
        [Fact]
        public void Properties_AreSettable()
        {
            var token = new SocialOauthAccessToken
            {
                ExpiresIn = 3600,
                TokenType = "Bearer",
                AccessToken = "access-1",
                IdToken = "id-1",
                RefreshToken = "refresh-1",
                Email = "u@example.com",
                ExternalProviderUserId = "ext-1"
            };

            token.ExpiresIn.Should().Be(3600);
            token.TokenType.Should().Be("Bearer");
            token.AccessToken.Should().Be("access-1");
            token.IdToken.Should().Be("id-1");
            token.RefreshToken.Should().Be("refresh-1");
            token.Email.Should().Be("u@example.com");
            token.ExternalProviderUserId.Should().Be("ext-1");
        }

        [Fact]
        public void TwitterOauthAccessToken_PropertiesAreSettable()
        {
            var token = new TwitterOauthAccessToken
            {
                TokenType = "Bearer",
                ExpiresIn = 7200,
                AccessToken = "access-1",
                RefreshToken = "refresh-1"
            };

            token.TokenType.Should().Be("Bearer");
            token.ExpiresIn.Should().Be(7200);
            token.AccessToken.Should().Be("access-1");
            token.RefreshToken.Should().Be("refresh-1");
        }
    }
}