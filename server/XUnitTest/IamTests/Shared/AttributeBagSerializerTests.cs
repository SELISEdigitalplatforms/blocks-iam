using System.Text.Json;
using FluentAssertions;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// Unit tests for <see cref="Iam.DomainService.Shared.Serialization.AttributeBagSerializer"/>,
    /// driven through <see cref="Organization"/> so they cover the member attribute wiring as well
    /// as the serializer itself.
    /// <para>
    /// The regression these guard: GET /api/iam/organizations returned 500 with
    /// "Unknown discriminator value 'JsonElement'" because the permissive object serializer had
    /// written <c>{ "_t": "JsonElement" }</c> into the Attributes bag, and one such row failed the
    /// deserialization of the whole result batch.
    /// </para>
    /// </summary>
    public class AttributeBagSerializerTests
    {
        static AttributeBagSerializerTests()
        {
            // Mirrors Blocks.Genesis.ApplicationConfigurations.ConfigureServices. Registration is
            // process-wide and one-shot, hence the static constructor and the swallow: another
            // fixture may already have registered an object serializer.
            try
            {
                BsonSerializer.RegisterSerializer<object>(new ObjectSerializer(_ => true));
            }
            catch (BsonSerializationException)
            {
                // Already registered by another test or by the driver defaults.
            }
        }

        private static Dictionary<string, object> Bind(string json) =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        private static BsonDocument StoredDocument(string attributesJson) =>
            BsonDocument.Parse("{ \"_id\": \"org-1\", \"Name\": \"Acme\", \"Attributes\": " + attributesJson + " }");

        [Fact]
        public void Deserialize_PoisonedDiscriminatorRow_DoesNotThrow()
        {
            var stored = StoredDocument("{ \"additionalProp1\": { \"_t\": \"JsonElement\" }, \"additionalProp2\": { \"_t\": \"JsonElement\" } }");

            var organization = BsonSerializer.Deserialize<Organization>(stored);

            // The values were never persisted - only the discriminator was - so there is nothing to
            // hand back. What matters is that the read completes instead of failing the batch.
            organization.Name.Should().Be("Acme");
            organization.Attributes.Should().HaveCount(2);
            organization.Attributes["additionalProp1"].Should().BeAssignableTo<Dictionary<string, object>>().Which.Should().BeEmpty();
        }

        [Fact]
        public void Deserialize_WrappedValue_UnwrapsPayload()
        {
            var stored = StoredDocument("{ \"meta\": { \"_t\": \"Dictionary`2\", \"_v\": { \"region\": \"eu\" } } }");

            var organization = BsonSerializer.Deserialize<Organization>(stored);

            organization.Attributes["meta"].Should().BeAssignableTo<Dictionary<string, object>>()
                .Which["region"].Should().Be("eu");
        }

        [Fact]
        public void Deserialize_PlainValues_MapToClr()
        {
            var stored = StoredDocument("{ \"plan\": \"pro\", \"seats\": 10, \"trial\": true, \"revenue\": 1250.5, \"tags\": [\"a\", \"b\"] }");

            var attributes = BsonSerializer.Deserialize<Organization>(stored).Attributes;

            attributes["plan"].Should().Be("pro");
            attributes["seats"].Should().Be(10);
            attributes["trial"].Should().Be(true);
            attributes["revenue"].Should().Be(1250.5);
            attributes["tags"].Should().BeEquivalentTo(new[] { "a", "b" });
        }

        [Fact]
        public void Deserialize_MissingAndNullAttributes_YieldEmptyBag()
        {
            BsonSerializer.Deserialize<Organization>(BsonDocument.Parse("{ \"_id\": \"org-1\", \"Name\": \"Acme\" }"))
                .Attributes.Should().BeEmpty();

            BsonSerializer.Deserialize<Organization>(StoredDocument("null"))
                .Attributes.Should().BeEmpty();
        }

        [Fact]
        public void Serialize_JsonBoundValues_WriteRealBsonNotDiscriminators()
        {
            var organization = new Organization
            {
                ItemId = "org-1",
                Name = "Acme",
                Attributes = Bind("{\"plan\":\"pro\",\"seats\":10,\"trial\":true,\"revenue\":1250.5}"),
            };

            var written = organization.ToBsonDocument()["Attributes"].AsBsonDocument;

            written["plan"].Should().Be(BsonValue.Create("pro"));
            written["seats"].Should().Be(BsonValue.Create(10L));
            written["trial"].Should().Be(BsonValue.Create(true));
            written["revenue"].Should().Be(BsonValue.Create(1250.5));
            written.Names.Should().NotContain("_t");
        }

        [Fact]
        public void Serialize_NestedJsonBoundValues_StayQueryable()
        {
            var organization = new Organization
            {
                ItemId = "org-1",
                Name = "Acme",
                Attributes = Bind("{\"meta\":{\"region\":\"eu\",\"tier\":2},\"tags\":[\"a\",\"b\"]}"),
            };

            var written = organization.ToBsonDocument()["Attributes"].AsBsonDocument;

            // A "_v" here would turn Attributes.meta.region into Attributes.meta._v.region and
            // break every dotted query against the bag.
            written["meta"].AsBsonDocument.Names.Should().BeEquivalentTo(new[] { "region", "tier" });
            written["meta"]["region"].Should().Be(BsonValue.Create("eu"));
            written["meta"]["tier"].Should().Be(BsonValue.Create(2L));
            written["tags"].AsBsonArray.Should().BeEquivalentTo(new BsonArray { "a", "b" });
        }

        [Fact]
        public void RoundTrip_JsonBoundValues_SurvivesRead()
        {
            var organization = new Organization
            {
                ItemId = "org-1",
                Name = "Acme",
                Attributes = Bind("{\"plan\":\"pro\",\"seats\":10,\"meta\":{\"region\":\"eu\"}}"),
            };

            var attributes = BsonSerializer.Deserialize<Organization>(organization.ToBsonDocument()).Attributes;

            attributes["plan"].Should().Be("pro");
            attributes["seats"].Should().Be(10L);
            attributes["meta"].Should().BeAssignableTo<Dictionary<string, object>>()
                .Which["region"].Should().Be("eu");
        }
    }
}
