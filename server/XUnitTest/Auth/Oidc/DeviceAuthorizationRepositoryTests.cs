using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// Unit tests for <see cref="DeviceAuthorizationRepository"/>. The single collection resolves from
    /// <see cref="IDbContextProvider.GetDatabase"/>; state-transition guards and result mapping are covered.
    /// </summary>
    public sealed class DeviceAuthorizationRepositoryTests
    {
        private const string CollectionName = "DeviceAuthorizationRequests";

        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IMongoDatabase> _database = MongoMock.Database();

        private Mock<IMongoCollection<DeviceAuthorizationRequestModel>> Register(IEnumerable<DeviceAuthorizationRequestModel>? items = null)
        {
            var col = MongoMock.Collection(items);
            MongoMock.OnDatabase(_database, CollectionName, col);
            _db.Setup(d => d.GetDatabase()).Returns(_database.Object);
            return col;
        }

        private DeviceAuthorizationRepository Sut() =>
            new(_db.Object, NullLogger<DeviceAuthorizationRepository>.Instance);

        private static DeviceAuthorizationRequestModel Entity(string id = "d1") =>
            new() { Id = id, ClientId = "c1", DeviceCodeHash = "hash", UserCode = "ABCD", ExpiresAt = DateTime.UtcNow.AddMinutes(10) };

        [Fact]
        public async Task EnsureIndexesAsync_CreatesIndexes()
        {
            var col = Register();
            MongoMock.SetupIndexes(col);
            await Sut().EnsureIndexesAsync();
            col.Verify(c => c.Indexes, Times.AtLeastOnce);
        }

        [Fact]
        public async Task EnsureIndexesAsync_SwallowsExceptions()
        {
            var col = Register();
            col.Setup(c => c.Indexes).Throws(new InvalidOperationException("boom"));
            await Sut().Invoking(s => s.EnsureIndexesAsync()).Should().NotThrowAsync();
        }

        [Fact]
        public async Task CreateAsync_Inserts()
        {
            var col = Register();
            await Sut().CreateAsync(Entity());
            col.Verify(c => c.InsertOneAsync(It.IsAny<DeviceAuthorizationRequestModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetByDeviceCodeHashAsync_ReturnsMatch()
        {
            Register(new[] { Entity() });
            (await Sut().GetByDeviceCodeHashAsync("hash"))!.Id.Should().Be("d1");
        }

        [Fact]
        public async Task GetByUserCodeAsync_ReturnsMatch()
        {
            Register(new[] { Entity() });
            (await Sut().GetByUserCodeAsync("ABCD"))!.Id.Should().Be("d1");
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsMatch()
        {
            Register(new[] { Entity() });
            (await Sut().GetByIdAsync("d1"))!.Id.Should().Be("d1");
        }

        [Fact]
        public async Task MarkApprovedAsync_ReturnsTrue()
        {
            Register();
            (await Sut().MarkApprovedAsync("d1", "u1", DateTime.UtcNow)).Should().BeTrue();
        }

        [Fact]
        public async Task MarkDeniedAsync_ReturnsTrue()
        {
            Register();
            (await Sut().MarkDeniedAsync("d1", DateTime.UtcNow)).Should().BeTrue();
        }

        [Fact]
        public async Task MarkConsumedAsync_ReturnsTrue()
        {
            Register();
            (await Sut().MarkConsumedAsync("d1", DateTime.UtcNow)).Should().BeTrue();
        }

        [Fact]
        public async Task MarkExpiredAsync_Empty_ReturnsFalse()
        {
            Register();
            (await Sut().MarkExpiredAsync(new[] { " ", "" })).Should().BeFalse();
        }

        [Fact]
        public async Task MarkExpiredAsync_Valid_ReturnsTrue()
        {
            Register();
            (await Sut().MarkExpiredAsync(new[] { "d1", "d2" })).Should().BeTrue();
        }

        [Fact]
        public async Task UpdatePollAsync_ReturnsTrue()
        {
            Register();
            (await Sut().UpdatePollAsync("d1", DateTime.UtcNow, 3)).Should().BeTrue();
        }

        [Fact]
        public async Task BumpPollIntervalAsync_Modified_ReturnsIncrementedInterval()
        {
            Register();
            (await Sut().BumpPollIntervalAsync("d1", 5)).Should().Be(10);
        }

        [Fact]
        public async Task BumpPollIntervalAsync_NotModified_ReturnsCurrentInterval()
        {
            var col = Register();
            col.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<DeviceAuthorizationRequestModel>>(),
                    It.IsAny<UpdateDefinition<DeviceAuthorizationRequestModel>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));
            (await Sut().BumpPollIntervalAsync("d1", 7)).Should().Be(7);
        }

        [Fact]
        public async Task GetExpiredIdsAsync_ReturnsNonEmptyIds()
        {
            Register(new[] { Entity("d1"), Entity("d2") });
            (await Sut().GetExpiredIdsAsync(DateTime.UtcNow, 100)).Should().BeEquivalentTo(new[] { "d1", "d2" });
        }
    }
}
