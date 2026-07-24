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
    /// Unit tests for <see cref="IdpSessionRepository"/>. Its "IdpSessions" collection resolves from
    /// <see cref="IDbContextProvider.GetDatabase"/>; create, account mutation, activity, revoke and
    /// query paths are exercised against mocks.
    /// </summary>
    public sealed class IdpSessionRepositoryTests
    {
        private const string CollectionName = "IdpSessions";

        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IMongoDatabase> _database = MongoMock.Database();

        private Mock<IMongoCollection<IdpSessionModel>> Register(IEnumerable<IdpSessionModel>? items = null)
        {
            var col = MongoMock.Collection(items);
            MongoMock.OnDatabase(_database, CollectionName, col);
            _db.Setup(d => d.GetDatabase()).Returns(_database.Object);
            return col;
        }

        private IdpSessionRepository Sut() =>
            new(_db.Object, NullLogger<IdpSessionRepository>.Instance);

        [Fact]
        public async Task CreateAsync_InsertsAndReturnsSessionId()
        {
            var col = Register();
            var session = new IdpSessionModel { SessionId = "s1" };
            (await Sut().CreateAsync(session)).Should().Be("s1");
            col.Verify(c => c.InsertOneAsync(It.IsAny<IdpSessionModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetBySessionIdAsync_ReturnsMatch()
        {
            Register(new[] { new IdpSessionModel { SessionId = "s1" } });
            (await Sut().GetBySessionIdAsync("s1")).SessionId.Should().Be("s1");
        }

        [Fact]
        public async Task AddAccountAsync_ReturnsTrue()
        {
            Register();
            (await Sut().AddAccountAsync("s1", new IdpSessionAccount { UserId = "u1", TenantId = "t1" })).Should().BeTrue();
        }

        [Fact]
        public async Task RemoveAccountAsync_ReturnsTrue()
        {
            Register();
            (await Sut().RemoveAccountAsync("s1", "u1", "t1")).Should().BeTrue();
        }

        [Fact]
        public async Task UpdateActivityAsync_ReturnsTrue()
        {
            Register();
            (await Sut().UpdateActivityAsync("s1")).Should().BeTrue();
        }

        [Fact]
        public async Task RevokeAsync_ReturnsTrue()
        {
            Register();
            (await Sut().RevokeAsync("s1")).Should().BeTrue();
        }

        [Fact]
        public async Task DeleteAsync_ReturnsTrue()
        {
            Register();
            (await Sut().DeleteAsync("s1")).Should().BeTrue();
        }

        [Fact]
        public async Task GetByUserAsync_ReturnsList()
        {
            Register(new[] { new IdpSessionModel { SessionId = "s1", TenantId = "t1" } });
            (await Sut().GetByUserAsync("u1", "t1")).Should().HaveCount(1);
        }
    }
}
