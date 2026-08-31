using System.Text.Json;
using FluentAssertions;
using Iam.DomainService.Accounts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace XUnitTest.IamTests.Accounts
{
    /// <summary>
    /// Unit tests for <see cref="SignupAttributeNormalizer"/>.
    /// <para>
    /// The whole point of this type is what the MongoDB driver ends up writing, so the tests
    /// register the same permissive object serializer Genesis installs at startup and assert on
    /// the serialized BSON, not just on the CLR values.
    /// </para>
    /// </summary>
    public class SignupAttributeNormalizerTests
    {
        static SignupAttributeNormalizerTests()
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

        [Fact]
        public void Normalize_Null_ReturnsEmpty()
        {
            SignupAttributeNormalizer.Normalize(null).Should().BeEmpty();
        }

        [Fact]
        public void Normalize_Scalars_ConvertToClrValues()
        {
            var result = SignupAttributeNormalizer.Normalize(
                Bind("{\"plan\":\"pro\",\"seats\":10,\"trial\":true,\"off\":false,\"revenue\":1250.5}"));

            result["plan"].Should().Be("pro");
            result["seats"].Should().Be(10L);
            result["trial"].Should().Be(true);
            result["off"].Should().Be(false);
            result["revenue"].Should().Be(1250.5);
        }

        [Fact]
        public void Normalize_Integer_StaysIntegerNotDouble()
        {
            // Regression: a ternary returning long on one branch and double on the other is
            // unified to double by the compiler, silently storing 10 as 10.0.
            var result = SignupAttributeNormalizer.Normalize(Bind("{\"seats\":10}"));

            result["seats"].Should().BeOfType<long>();
            result["seats"].Should().Be(10L);
        }

        [Fact]
        public void Normalize_NullValues_AreDropped()
        {
            SignupAttributeNormalizer.Normalize(Bind("{\"nothing\":null,\"plan\":\"pro\"}"))
                .Should().ContainKey("plan").And.NotContainKey("nothing");
        }

        [Fact]
        public void Normalize_IllegalMongoKeys_AreDropped()
        {
            var result = SignupAttributeNormalizer.Normalize(
                Bind("{\"bad.key\":\"x\",\"$evil\":\"y\",\"good\":\"z\"}"));

            result.Should().ContainKey("good");
            result.Should().NotContainKey("bad.key");
            result.Should().NotContainKey("$evil");
        }

        [Fact]
        public void Normalize_TooManyKeys_AreCappedAt25()
        {
            var raw = new Dictionary<string, object>();
            for (var i = 0; i < 60; i++)
            {
                raw[$"k{i}"] = $"v{i}";
            }

            SignupAttributeNormalizer.Normalize(raw).Should().HaveCount(25);
        }

        [Fact]
        public void Normalize_OverlongKey_IsDropped()
        {
            var raw = new Dictionary<string, object> { { new string('k', 65), "v" }, { "ok", "v" } };

            SignupAttributeNormalizer.Normalize(raw).Should().ContainKey("ok").And.HaveCount(1);
        }

        [Fact]
        public void Normalize_OverlongString_IsTruncated()
        {
            var raw = new Dictionary<string, object> { { "bio", new string('x', 900) } };

            ((string)SignupAttributeNormalizer.Normalize(raw)["bio"]).Should().HaveLength(512);
        }

        [Fact]
        public void Normalize_ScalarArray_IsPreserved()
        {
            var result = SignupAttributeNormalizer.Normalize(Bind("{\"tags\":[\"a\",\"b\",1]}"));

            result["tags"].Should().BeOfType<List<object>>();
            ((List<object>)result["tags"]).Should().Equal("a", "b", 1L);
        }

        [Fact]
        public void Normalize_NestedObject_IsKeptAsRawJson()
        {
            var result = SignupAttributeNormalizer.Normalize(Bind("{\"meta\":{\"region\":\"eu\"}}"));

            result["meta"].Should().BeOfType<string>();
            ((string)result["meta"]).Should().Contain("region").And.Contain("eu");
        }

        [Fact]
        public void Normalize_AlreadyClrValues_PassThroughUnchanged()
        {
            // The SSO mappers build their bags server-side, so values are not JsonElement.
            var raw = new Dictionary<string, object> { { "provider", "google" }, { "count", 3 } };

            var result = SignupAttributeNormalizer.Normalize(raw);

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
            var normalized = SignupAttributeNormalizer.Normalize(
                Bind("{\"plan\":\"pro\",\"seats\":10,\"trial\":true,\"tags\":[\"a\",\"b\"]}"));

            var json = new BsonDocument("Attributes", normalized.ToBsonDocument()).ToJson();

            json.Should().Contain("pro");
            json.Should().NotContain("_t", "no type discriminators should survive normalization");
        }
    }
}
