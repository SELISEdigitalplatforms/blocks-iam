using System.Text.Json;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.SocialServices;
using FluentAssertions;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class ExternalUserMapperRegistryTests
    {
        private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement;

        [Fact]
        public void Map_UsesRegisteredMapper_ForKnownProvider()
        {
            var mockMapper = new Mock<IExternalUserMapper>();
            mockMapper.SetupGet(m => m.ProviderKey).Returns(SocialLogInTypes.Google);
            var googleMapper = new GoogleExternalUserMapper();

            var registry = new ExternalUserMapperRegistry([mockMapper.Object, googleMapper]);
            var json = ParseJson(@"{ ""sub"": ""google-id"", ""email"": ""g@e.com"" }");
            var user = new BYOSsoUserData();

            registry.Map(SocialLogInTypes.Google, json, user);

            mockMapper.Verify(m => m.Map(json, user), Times.Once);
        }

        [Fact]
        public void Map_IsCaseInsensitive()
        {
            var mapper = new Mock<IExternalUserMapper>();
            mapper.SetupGet(m => m.ProviderKey).Returns(SocialLogInTypes.Google);

            var registry = new ExternalUserMapperRegistry([mapper.Object]);
            var json = ParseJson(@"{ ""sub"": ""x"" }");
            var user = new BYOSsoUserData();

            registry.Map("GOOGLE", json, user);

            mapper.Verify(m => m.Map(json, user), Times.Once);
        }

        [Fact]
        public void Map_UsesGenericFallback_ForUnknownProvider()
        {
            var registry = new ExternalUserMapperRegistry([]);
            var json = ParseJson(@"{ ""sub"": ""generic-id"", ""email"": ""g@e.com"" }");
            var user = new BYOSsoUserData();

            registry.Map("unknown-provider", json, user);

            user.ExternalProviderUserId.Should().Be("generic-id");
            user.Email.Should().Be("g@e.com");
        }

        [Fact]
        public void Map_ExcludesGenericMapper_FromDispatchDictionary()
        {
            var mockMapper = new Mock<IExternalUserMapper>();
            mockMapper.SetupGet(m => m.ProviderKey).Returns(SocialLogInTypes.Google);
            var generic = new GenericOidcExternalUserMapper();

            var registry = new ExternalUserMapperRegistry([mockMapper.Object, generic]);
            var json = ParseJson(@"{ ""sub"": ""google-id"" }");
            var user = new BYOSsoUserData();

            registry.Map(SocialLogInTypes.Google, json, user);

            mockMapper.Verify(m => m.Map(json, user), Times.Once);
        }

        [Fact]
        public void Map_HandlesEmptyMapperList_UsingDefaultGeneric()
        {
            var registry = new ExternalUserMapperRegistry([]);
            var json = ParseJson(@"{ ""sub"": ""x-id"", ""email"": ""x@e.com"" }");
            var user = new BYOSsoUserData();

            registry.Map("anything", json, user);

            user.ExternalProviderUserId.Should().Be("x-id");
        }
    }
}