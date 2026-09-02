using Authentication.DomainService.Entities;
using Authentication.DomainService.Migrations;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.RequestModel;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace XUnitTest.Auth
{
    public sealed class OidcClientBrandingRemovalTests
    {
        private static readonly string[] RetiredNames =
        [
            "LogoUri", "UiBrandColor", "ClientLogoUrl", "ClientBrandColor"
        ];

        [Fact]
        public void OidcClientRegistration_NoLongerExposesAnyRetiredBrandingProperty()
        {
            var propertyNames = typeof(OidcClientRegistration).GetProperties().Select(property => property.Name);

            propertyNames.Should().NotIntersectWith(RetiredNames);
        }

        [Fact]
        public void SaveRequest_NoLongerExposesAnyRetiredBrandingProperty()
        {
            var propertyNames = typeof(SaveOIDCClientRequest).GetProperties().Select(property => property.Name);

            propertyNames.Should().NotIntersectWith(RetiredNames);
        }

        [Fact]
        public void Validator_NoLongerContainsRuleForRetiredBrandingProperties()
        {
            var members = new SaveOIDCClientRequestValidator()
                .CreateDescriptor()
                .GetMembersWithValidators()
                .Select(member => member.Key);

            members.Should().NotIntersectWith(RetiredNames);
        }

        [Fact]
        public void OldBsonDocument_DeserializesAndIgnoresRetiredFields()
        {
            var document = new BsonDocument
            {
                ["_id"] = "client-1",
                ["ClientId"] = "client-1",
                ["LogoUri"] = "https://legacy.example/logo.png",
                ["UiBrandColor"] = "#123456",
                ["UnexpectedFutureField"] = true
            };

            var action = () => BsonSerializer.Deserialize<OidcClientRegistration>(document);

            var result = action.Should().NotThrow().Subject;
            result.ClientId.Should().Be("client-1");
            result.GetType().GetProperty("LogoUri").Should().BeNull();
            result.GetType().GetProperty("UiBrandColor").Should().BeNull();
        }

        [Fact]
        public void NewClientBson_DoesNotWriteRetiredBrandingFields()
        {
            var document = new OidcClientRegistration { ItemId = "client-1", ClientId = "client-1" }.ToBsonDocument();

            RetiredNames.Should().AllSatisfy(name => document.Contains(name).Should().BeFalse());
        }

        [Fact]
        public void LegacyMigrationSnapshot_MapsRetiredBsonFieldsWithoutReintroducingDomainProperties()
        {
            var document = new BsonDocument
            {
                ["_id"] = "client-1",
                ["ClientId"] = "client-1",
                ["IsActive"] = true,
                ["LogoUri"] = "https://legacy.example/logo.png",
                ["UiBrandColor"] = "#abc"
            };

            var snapshot = BsonSerializer.Deserialize<LegacyOidcClientBranding>(document);

            snapshot.ClientId.Should().Be("client-1");
            snapshot.IsActive.Should().BeTrue();
            snapshot.LegacyLogoUrl.Should().Be("https://legacy.example/logo.png");
            snapshot.LegacyBrandColor.Should().Be("#abc");
        }
    }
}
