using Blocks.Genesis;
using FluentAssertions;
using Mfa.DomainService.Services;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Mfa
{
    /// <summary>
    /// Unit tests for <see cref="MfaManagementRepository"/>. Its generic collection accessor resolves
    /// through <see cref="IDbContextProvider"/>, mocked here to return an in-memory collection.
    /// </summary>
    public sealed class MfaManagementRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _db = new();

        private Mock<IMongoCollection<Sample>> Register(IEnumerable<Sample>? items = null)
        {
            var col = MongoMock.Collection(items);
            _db.Setup(d => d.GetCollection<Sample>(It.IsAny<string>())).Returns(col.Object);
            return col;
        }

        private MfaManagementRepository Sut() => new(_db.Object);

        [Fact]
        public async Task DeleteItemsAsync_DeletesMany()
        {
            var col = Register();
            await Sut().DeleteItemsAsync<Sample>(x => x.Name == "n");
            col.Verify(c => c.DeleteManyAsync(It.IsAny<FilterDefinition<Sample>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetItemsAsync_ReturnsList()
        {
            Register(new[] { new Sample { Id = "1", Name = "n" } });
            (await Sut().GetItemsAsync<Sample>(x => x.Name == "n")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetItemsAsync_WithCollectionName_ReturnsList()
        {
            Register(new[] { new Sample { Id = "1", Name = "n" } });
            (await Sut().GetItemsAsync<Sample>(x => x.Name == "n", "CustomCollection")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetItemAsync_ReturnsMatch()
        {
            Register(new[] { new Sample { Id = "1", Name = "n" } });
            (await Sut().GetItemAsync<Sample>(x => x.Id == "1")).Id.Should().Be("1");
        }

        [Fact]
        public async Task SaveAsync_Single_Inserts()
        {
            var col = Register();
            await Sut().SaveAsync(new Sample { Id = "1" });
            col.Verify(c => c.InsertOneAsync(It.IsAny<Sample>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveAsync_SingleWithCollectionName_Inserts()
        {
            var col = Register();
            await Sut().SaveAsync(new Sample { Id = "1" }, "CustomCollection");
            col.Verify(c => c.InsertOneAsync(It.IsAny<Sample>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveAsync_List_InsertsMany()
        {
            var col = Register();
            await Sut().SaveAsync(new List<Sample> { new() { Id = "1" }, new() { Id = "2" } });
            col.Verify(c => c.InsertManyAsync(It.IsAny<IEnumerable<Sample>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpsertAsync_Replaces_WithUpsert()
        {
            var col = Register();
            await Sut().UpsertAsync(new Sample { Id = "1" }, x => x.Id == "1");
            col.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<Sample>>(), It.IsAny<Sample>(),
                It.Is<ReplaceOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        public sealed class Sample
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }
    }
}
