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
    /// Unit tests for <see cref="AuthorizationCodeRepository"/>. The "IdpAuthorizationCodes" collection
    /// resolves from <see cref="IDbContextProvider.GetDatabase"/>.
    /// </summary>
    public sealed class AuthorizationCodeRepositoryTests
    {
        private const string CollectionName = "IdpAuthorizationCodes";

        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IMongoDatabase> _database = MongoMock.Database();

        private Mock<IMongoCollection<AuthorizationCodeModel>> Register(IEnumerable<AuthorizationCodeModel>? items = null)
        {
            var col = MongoMock.Collection(items);
            MongoMock.OnDatabase(_database, CollectionName, col);
            _db.Setup(d => d.GetDatabase()).Returns(_database.Object);
            return col;
        }

        private AuthorizationCodeRepository Sut() =>
            new(_db.Object, NullLogger<AuthorizationCodeRepository>.Instance);

        [Fact]
        public async Task CreateAsync_InsertsAndReturnsCode()
        {
            var col = Register();
            var code = new AuthorizationCodeModel { Code = "abc", UserId = "u1", ClientId = "c1" };
            (await Sut().CreateAsync(code)).Should().Be("abc");
            col.Verify(c => c.InsertOneAsync(It.IsAny<AuthorizationCodeModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetByCodeAsync_ReturnsMatch()
        {
            Register(new[] { new AuthorizationCodeModel { Code = "abc", UserId = "u1" } });
            (await Sut().GetByCodeAsync("abc")).UserId.Should().Be("u1");
        }

        [Fact]
        public async Task DeleteAsync_ReturnsTrue()
        {
            Register();
            (await Sut().DeleteAsync("abc")).Should().BeTrue();
        }

        [Fact]
        public async Task GetExpiredAsync_ReturnsList()
        {
            Register(new[] { new AuthorizationCodeModel { Code = "abc" } });
            (await Sut().GetExpiredAsync()).Should().HaveCount(1);
        }
    }
}
