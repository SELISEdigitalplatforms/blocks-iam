using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// Unit tests for <see cref="TokenRevocationRepository"/> (RFC 7009 JTI blacklist). The
    /// "IdpRevokedTokens" collection resolves from <see cref="IDbContextProvider.GetDatabase"/>.
    /// </summary>
    public sealed class TokenRevocationRepositoryTests
    {
        private const string CollectionName = "IdpRevokedTokens";

        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IMongoDatabase> _database = MongoMock.Database();

        private Mock<IMongoCollection<TokenRevocationModel>> Register(IEnumerable<TokenRevocationModel>? items = null)
        {
            var col = MongoMock.Collection(items);
            MongoMock.OnDatabase(_database, CollectionName, col);
            _db.Setup(d => d.GetDatabase()).Returns(_database.Object);
            return col;
        }

        private TokenRevocationRepository Sut() =>
            new(_db.Object, NullLogger<TokenRevocationRepository>.Instance);

        [Fact]
        public async Task RevokeTokenAsync_InsertsAndReturnsTrue()
        {
            var col = Register();
            (await Sut().RevokeTokenAsync("jti1", "u1", "logout", DateTime.UtcNow.AddHours(1))).Should().BeTrue();
            col.Verify(c => c.InsertOneAsync(
                It.Is<TokenRevocationModel>(m => m.Jti == "jti1" && m.RevokeReason == "logout"),
                It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IsRevokedAsync_ReturnsTrue_WhenFound()
        {
            Register(new[] { new TokenRevocationModel { Jti = "jti1" } });
            (await Sut().IsRevokedAsync("jti1")).Should().BeTrue();
        }

        [Fact]
        public async Task IsRevokedAsync_ReturnsFalse_WhenNotFound()
        {
            Register();
            (await Sut().IsRevokedAsync("jti1")).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteAsync_ReturnsTrue()
        {
            Register();
            (await Sut().DeleteAsync("jti1")).Should().BeTrue();
        }

        [Fact]
        public async Task GetRevocationDetailsAsync_ReturnsMatch()
        {
            Register(new[] { new TokenRevocationModel { Jti = "jti1", UserId = "u1" } });
            (await Sut().GetRevocationDetailsAsync("jti1")).UserId.Should().Be("u1");
        }

        [Fact]
        public async Task GetRevokedTokensByUserAsync_ReturnsList()
        {
            Register(new[] { new TokenRevocationModel { Jti = "jti1", UserId = "u1" } });
            (await Sut().GetRevokedTokensByUserAsync("u1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetByUserAsync_DelegatesToGetRevokedTokens()
        {
            Register(new[] { new TokenRevocationModel { Jti = "jti1", UserId = "u1" } });
            (await Sut().GetByUserAsync("u1")).Should().HaveCount(1);
        }
    }
}
