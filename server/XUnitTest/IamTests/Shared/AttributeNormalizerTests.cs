using System.Text.Json;
using FluentAssertions;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Shared.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// Unit tests for <see cref="AttributeNormalizer"/>.
    /// <para>
    /// The whole point of this type is what the MongoDB driver ends up writing, so the tests
    /// register the same permissive object serializer Genesis installs at startup and assert on the
    /// serialized BSON, not just on the CLR values.
    /// </para>
    /// </summary>
    public class AttributeNormalizerTests
    {
        static AttributeNormalizerTests()
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

        private static Dictionary<string, object> Public(string json) =>
            AttributeNormalizer.Normalize(Bind(json), AttributePolicy.Public);

        private static Dictionary<string, object> Internal(string json) =>
            AttributeNormalizer.Normalize(Bind(json), AttributePolicy.Internal);

        [Fact]
        public void Normalize_Null_ReturnsEmpty()
        {
            AttributeNormalizer.Normalize(null, AttributePolicy.Public).Should().BeEmpty();
        }

        [Fact]
        public void Normalize_Scalars_ConvertToClrValues()
        {
            var result = Public("{\"plan\":\"pro\",\"seats\":10,\"trial\":true,\"off\":false,\"revenue\":1250.5}");

            result["plan"].Should().Be("pro");
            result["seats"].Should().Be(10L);
            result["trial"].Should().Be(true);
            result["off"].Should().Be(false);
            result["revenue"].Should().Be(1250.5);
        }

        [Fact]
        public void Normalize_Integer_StaysIntegerNotDouble()
        {
            // Regression: a ternary returning long on one branch and double on the other is unified
            // to double by the compiler, which silently turns every 10 into 10.0.
            Public("{\"seats\":10}")["seats"].Should().BeOfType<long>();
        }

        [Fact]
        public void Normalize_NullValues_AreDropped()
        {
            Public("{\"nothing\":null,\"plan\":\"pro\"}").Should().ContainKey("plan").And.HaveCount(1);
        }

        [Fact]
        public void Normalize_ScalarArray_IsPreserved()
        {
            var result = Public("{\"tags\":[\"a\",\"b\",1]}");

            result["tags"].Should().BeOfType<List<object>>();
            ((List<object>)result["tags"]).Should().Equal("a", "b", 1L);
        }

        [Fact]
        public void Normalize_NestedObject_IsKeptAsSubdocument()
        {
            // Previously flattened to a raw JSON string, which made Attributes.meta.region
            // unqueryable. Real nesting is the supported shape now.
            var result = Public("{\"meta\":{\"region\":\"eu\",\"tier\":2}}");

            var meta = result["meta"].Should().BeOfType<Dictionary<string, object>>().Subject;
            meta["region"].Should().Be("eu");
            meta["tier"].Should().Be(2L);
        }

        [Fact]
        public void Normalize_ObjectInsideArray_IsPreserved()
        {
            var result = Internal("{\"contacts\":[{\"kind\":\"work\"},{\"kind\":\"home\"}]}");

            var contacts = result["contacts"].Should().BeOfType<List<object>>().Subject;
            contacts.Should().HaveCount(2);
            ((Dictionary<string, object>)contacts[0])["kind"].Should().Be("work");
        }

        [Fact]
        public void Normalize_BeyondDepthCap_IsDropped()
        {
            // Public allows 3 levels: the bag, meta, and a. "b" sits one level too deep.
            var result = Public("{\"meta\":{\"a\":{\"b\":{\"c\":\"too deep\"}}}}");

            var a = (Dictionary<string, object>)((Dictionary<string, object>)result["meta"])["a"];
            a.Should().BeEmpty();
        }

        [Fact]
        public void Normalize_TooManyKeys_IsCappedByPolicy()
        {
            var raw = new Dictionary<string, object>();
            for (var i = 0; i < 40; i++)
            {
                raw[$"key{i}"] = "v";
            }

            AttributeNormalizer.Normalize(raw, AttributePolicy.Public).Should().HaveCount(25);
            AttributeNormalizer.Normalize(raw, AttributePolicy.Internal).Should().HaveCount(40);
        }

        [Theory]
        [InlineData("$bad")]
        [InlineData("dotted.key")]
        [InlineData("   ")]
        public void Normalize_UnusableKeys_AreDropped(string key)
        {
            // '$' is rejected by Mongo outright; '.' is read as a path separator on query and would
            // silently address a nested field that does not exist.
            var raw = new Dictionary<string, object> { { key, "v" }, { "ok", "v" } };

            AttributeNormalizer.Normalize(raw, AttributePolicy.Public)
                .Should().ContainKey("ok").And.HaveCount(1);
        }

        [Fact]
        public void Normalize_OverlongString_IsTruncatedToPolicyLimit()
        {
            var raw = new Dictionary<string, object> { { "bio", new string('x', 9000) } };

            ((string)AttributeNormalizer.Normalize(raw, AttributePolicy.Public)["bio"]).Should().HaveLength(512);
            ((string)AttributeNormalizer.Normalize(raw, AttributePolicy.Internal)["bio"]).Should().HaveLength(8192);
        }

        [Fact]
        public void Normalize_AlreadyClrValues_PassThroughUnchanged()
        {
            // The SSO mappers build their bags server-side, so values are not JsonElement.
            var raw = new Dictionary<string, object> { { "provider", "google" }, { "count", 3 } };

            var result = AttributeNormalizer.Normalize(raw, AttributePolicy.Internal);

            result["provider"].Should().Be("google");
            result["count"].Should().Be(3);
        }

        [Fact]
        public void RawJsonElements_SerializeToEmptyMarkers_WithoutNormalization()
        {
            // Documents the defect this type exists to prevent: no exception, values gone.
            var document = new BsonDocument("Attributes", Bind("{\"plan\":\"pro\"}").ToBsonDocument());

            document.ToJson().Should().Contain("_t").And.NotContain("pro");
        }

        [Fact]
        public void NormalizedValues_SerializeToRealBsonValues()
        {
            var organization = new Organization
            {
                ItemId = "org-1",
                Name = "Acme",
                Attributes = Public("{\"plan\":\"pro\",\"seats\":10,\"trial\":true,\"tags\":[\"a\",\"b\"],\"meta\":{\"region\":\"eu\"}}"),
            };

            var json = organization.ToBsonDocument()["Attributes"].ToJson();

            json.Should().Contain("pro").And.Contain("eu");
            json.Should().NotContain("_t", "no type discriminators should survive the write path");
        }

        [Fact]
        public void NestedMap_WithoutTheSerializer_StillPicksUpAWrapper()
        {
            // Why AttributeBagSerializer is mandatory rather than belt-and-braces. Normalization
            // alone produces a nested Dictionary, and the permissive object serializer wraps that in
            // _t/_v - which turns Attributes.meta.region into Attributes.meta._v.region and breaks
            // dotted queries. Any new entity with an attribute bag must carry the serializer too.
            var bare = new BsonDocument("Attributes", Public("{\"meta\":{\"region\":\"eu\"}}").ToBsonDocument());

            bare.ToJson().Should().Contain("_t");
        }
    }
}
