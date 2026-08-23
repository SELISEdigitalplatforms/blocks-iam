using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Captcha
{
    /// <summary>
    /// The dual read: the blocks-os key/value store first, the legacy secret document second.
    /// </summary>
    public sealed class CaptchaConfigurationRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContext = new();
        private readonly Mock<IKeyValueStore> _store = new();

        public CaptchaConfigurationRepositoryTests()
        {
            GivenStoreRecords();
            GivenLegacySecret(null);
        }

        private CaptchaConfigurationRepository Create() =>
            new(_dbContext.Object, _store.Object, NullLogger<CaptchaConfigurationRepository>.Instance);

        private void GivenStoreRecords(params BsonDocument[] documents) =>
            _store.Setup(s => s.GetByPrefixAsync<BsonDocument>("captcha_", true, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(documents);

        private static BsonDocument Record(
            string id, string provider, bool isEnable, string? secretId = "sec-1", string key = "site-key") =>
            new CaptchaConfigRecord
            {
                Id = id,
                Provider = provider,
                IsEnable = isEnable,
                CaptchaKey = key,
                CaptchaGenerator = "EasyCaptchaGenerator",
                SecretId = secretId
            }.ToBsonDocument();

        private void GivenLegacySecret(Secret? secret)
        {
            var cursor = new Mock<IAsyncCursor<Secret>>();
            var batch = secret is null ? new List<Secret>() : [secret];
            var moved = false;

            cursor.Setup(c => c.Current).Returns(batch);
            cursor.Setup(c => c.MoveNext(It.IsAny<CancellationToken>()))
                  .Returns(() => !moved && (moved = true));
            cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(() => !moved && (moved = true));

            var collection = new Mock<IMongoCollection<Secret>>();
            collection.Setup(m => m.FindAsync(
                        It.IsAny<FilterDefinition<Secret>>(),
                        It.IsAny<FindOptions<Secret, Secret>>(),
                        It.IsAny<CancellationToken>()))
                      .ReturnsAsync(cursor.Object);

            _dbContext.Setup(d => d.GetCollection<Secret>("Secrets")).Returns(collection.Object);
        }

        private static Secret LegacySecret(string provider = "recaptcha", string isEnable = "true") => new()
        {
            SecretKey = "captcha",
            KeyValuePairs = new Dictionary<string, string>
            {
                ["isEnable"] = isEnable,
                ["provider"] = provider,
                ["captchaKey"] = "legacy-site-key",
                ["captchaSecret"] = "legacy-secret",
                ["captchaGenerator"] = "HardCaptchaGenerator"
            }
        };

        [Fact]
        public async Task GetCaptchaConfigurationAsync_PrefersTheStoreRecord()
        {
            GivenStoreRecords(Record("aaa", "recaptcha", isEnable: true));

            var result = await Create().GetCaptchaConfigurationAsync();

            result.Should().NotBeNull();
            result!.CaptchaKey.Should().Be("site-key");
            result.SecretId.Should().Be("sec-1");
            // The store never carries a secret value; only the pointer.
            result.CaptchaSecret.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_WithSeveralEnabled_PicksTheLowestIdDeterministically()
        {
            GivenStoreRecords(
                Record("zzz", "hcaptcha", isEnable: true, key: "z-key"),
                Record("aaa", "recaptcha", isEnable: true, key: "a-key"));

            var repository = Create();

            for (var i = 0; i < 5; i++)
            {
                var result = await repository.GetCaptchaConfigurationAsync();
                result!.CaptchaKey.Should().Be("a-key");
            }
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_IgnoresDisabledStoreRecordsAndFallsBack()
        {
            GivenStoreRecords(Record("aaa", "recaptcha", isEnable: false));
            GivenLegacySecret(LegacySecret());

            var result = await Create().GetCaptchaConfigurationAsync();

            result.Should().NotBeNull();
            result!.CaptchaKey.Should().Be("legacy-site-key");
            result.CaptchaSecret.Should().Be("legacy-secret");
            result.SecretId.Should().BeNull();
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_WithBothSources_UsesTheStoreAndDoesNotMerge()
        {
            GivenStoreRecords(Record("aaa", "recaptcha", isEnable: true));
            GivenLegacySecret(LegacySecret());

            var result = await Create().GetCaptchaConfigurationAsync();

            result!.CaptchaKey.Should().Be("site-key");
            // Critically: no legacy secret leaks in alongside the new site key.
            result.CaptchaSecret.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_SkipsAMalformedRecordAndKeepsGoing()
        {
            var malformed = new BsonDocument { { "IsEnable", "not-a-bool" }, { "Id", 42 } };
            GivenStoreRecords(malformed, Record("bbb", "recaptcha", isEnable: true));

            var result = await Create().GetCaptchaConfigurationAsync();

            result.Should().NotBeNull();
            result!.CaptchaKey.Should().Be("site-key");
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_WhenTheStoreReadThrows_FallsBackToLegacy()
        {
            _store.Setup(s => s.GetByPrefixAsync<BsonDocument>("captcha_", true, It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("mongo down"));
            GivenLegacySecret(LegacySecret());

            var result = await Create().GetCaptchaConfigurationAsync();

            result!.CaptchaKey.Should().Be("legacy-site-key");
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_WithNothingAnywhere_ReturnsNull()
        {
            var result = await Create().GetCaptchaConfigurationAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_WithDisabledLegacyDocument_ReturnsNull()
        {
            GivenLegacySecret(LegacySecret(isEnable: "false"));

            var result = await Create().GetCaptchaConfigurationAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetByProviderAsync_MatchesTheStoreRecordCaseInsensitively()
        {
            GivenStoreRecords(Record("aaa", "ReCaptcha", isEnable: true));

            var result = await Create().GetByProviderAsync("recaptcha");

            result.Should().NotBeNull();
            result!.SecretId.Should().Be("sec-1");
        }

        [Fact]
        public async Task GetByProviderAsync_ReturnsDisabledStoreRecords()
        {
            // Pre-existing behaviour: the provider lookup does not filter on enablement, and
            // CaptchaService depends on that.
            GivenStoreRecords(Record("aaa", "recaptcha", isEnable: false));

            var result = await Create().GetByProviderAsync("recaptcha");

            result.Should().NotBeNull();
        }

        [Fact]
        public async Task GetByProviderAsync_FallsBackToLegacyWhenTheStoreHasNoSuchProvider()
        {
            GivenStoreRecords(Record("aaa", "hcaptcha", isEnable: true));
            GivenLegacySecret(LegacySecret());

            var result = await Create().GetByProviderAsync("recaptcha");

            result.Should().NotBeNull();
            result!.CaptchaSecret.Should().Be("legacy-secret");
        }

        [Fact]
        public async Task GetByProviderAsync_WithNullProvider_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => Create().GetByProviderAsync(null));
        }
    }
}
