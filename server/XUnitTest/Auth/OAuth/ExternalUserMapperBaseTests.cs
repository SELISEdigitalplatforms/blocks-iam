using Authentication.DomainService.OAuth.SocialServices;
using Authentication.DomainService.OAuth;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class ExternalUserMapperBaseTests
    {
        private class TestMapper : ExternalUserMapperBase
        {
            public override string ProviderKey => "test";
            public override void Map(System.Text.Json.JsonElement result, BYOSsoUserData user)
            {
                user.Email = GetString(result, "email", "mail");
            }
        }

        [Fact]
        public void GetString_ReturnsFirstMatchingKey()
        {
            var mapper = new TestMapper();
            var json = System.Text.Json.JsonDocument.Parse(@"{ ""email"": ""u@e.com"", ""mail"": ""backup@e.com"" }").RootElement;
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.Email.Should().Be("u@e.com");
        }

        [Fact]
        public void GetString_ReturnsSecondKey_WhenFirstMissing()
        {
            var mapper = new TestMapper();
            var json = System.Text.Json.JsonDocument.Parse(@"{ ""mail"": ""backup@e.com"" }").RootElement;
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.Email.Should().Be("backup@e.com");
        }

        [Fact]
        public void GetString_ReturnsEmpty_WhenNoKeysMatch()
        {
            var mapper = new TestMapper();
            var json = System.Text.Json.JsonDocument.Parse(@"{ ""other"": ""value"" }").RootElement;
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.Email.Should().BeEmpty();
        }

        [Fact]
        public void ProviderKey_AbstractProperty_Implemented()
        {
            new TestMapper().ProviderKey.Should().Be("test");
        }
    }
}