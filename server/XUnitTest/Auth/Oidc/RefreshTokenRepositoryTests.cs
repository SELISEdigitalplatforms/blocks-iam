using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
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
    /// Unit tests for <see cref="RefreshTokenRepository"/>. The repository resolves its single collection
    /// from <see cref="IDbContextProvider.GetDatabase"/>, mocked here so validation, filtering and
    /// revocation result mapping are covered without a live MongoDB.
    /// </summary>
    public sealed class RefreshTokenRepositoryTests
    {
        private const string CollectionName = "IdpRefreshTokens";

        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IAuthenticationRepository> _authRepo = new();
        private readonly Mock<IMongoDatabase> _database = MongoMock.Database();

        private Mock<IMongoCollection<RefreshTokenModel>> Register(IEnumerable<RefreshTokenModel>? items = null)
        {
            var col = MongoMock.Collection(items);
            MongoMock.OnDatabase(_database, CollectionName, col);
            _db.Setup(d => d.GetDatabase()).Returns(_database.Object);
            return col;
        }

        private RefreshTokenRepository Sut() =>
            new(_db.Object, _authRepo.Object, NullLogger<RefreshTokenRepository>.Instance);

        private static RefreshTokenModel Token(string id = "t1", string user = "u1", string tenant = "tenant1", string session = "s1") =>
            new()
            {
                TokenId = id,
                UserId = user,
                TenantId = tenant,
                SessionId = session,
                ClientId = "c1",
                IsRevoked = false,
                AbsoluteExpiry = DateTime.UtcNow.AddHours(1),
                IssuedUtc = DateTime.UtcNow
            };

        [Fact]
        public async Task CreateAsync_NullToken_Throws()
        {
            Register();
            await Assert.ThrowsAsync<ArgumentNullException>(() => Sut().CreateAsync(null!));
        }

        [Theory]
        [InlineData("", "u1", "tenant1", "s1")]
        [InlineData("t1", "", "tenant1", "s1")]
        [InlineData("t1", "u1", "", "s1")]
        [InlineData("t1", "u1", "tenant1", "")]
        public async Task CreateAsync_MissingRequiredField_Throws(string id, string user, string tenant, string session)
        {
            Register();
            var token = new RefreshTokenModel { TokenId = id, UserId = user, TenantId = tenant, SessionId = session };
            await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(token));
        }

        [Fact]
        public async Task CreateAsync_Valid_InsertsAndReturnsTokenId()
        {
            var col = Register();
            (await Sut().CreateAsync(Token())).Should().Be("t1");
            col.Verify(c => c.InsertOneAsync(It.IsAny<RefreshTokenModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetByTokenIdAsync_Empty_ReturnsNull()
        {
            Register();
            (await Sut().GetByTokenIdAsync(" ")).Should().BeNull();
        }

        [Fact]
        public async Task GetByTokenIdAsync_ReturnsMatch()
        {
            Register(new[] { Token() });
            (await Sut().GetByTokenIdAsync("t1")).TokenId.Should().Be("t1");
        }

        [Fact]
        public async Task GetBySessionIdAsync_Empty_ReturnsEmpty()
        {
            Register();
            (await Sut().GetBySessionIdAsync("")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetBySessionIdAsync_ReturnsList()
        {
            Register(new[] { Token() });
            (await Sut().GetBySessionIdAsync("s1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetActiveTokensBySessionIdAsync_Empty_ReturnsEmpty()
        {
            Register();
            (await Sut().GetActiveTokensBySessionIdAsync("")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetActiveTokensBySessionIdAsync_ReturnsList()
        {
            Register(new[] { Token() });
            (await Sut().GetActiveTokensBySessionIdAsync("s1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetActiveTokensByUserAsync_Empty_ReturnsEmpty()
        {
            Register();
            (await Sut().GetActiveTokensByUserAsync("")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetActiveTokensByUserAsync_ReturnsList()
        {
            Register(new[] { Token() });
            (await Sut().GetActiveTokensByUserAsync("u1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRotationHistoryAsync_Empty_ReturnsEmpty()
        {
            Register();
            (await Sut().GetRotationHistoryAsync("")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetRotationHistoryAsync_ReturnsList()
        {
            Register(new[] { Token() });
            (await Sut().GetRotationHistoryAsync("s1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetByUserAsync_ReturnsList()
        {
            Register(new[] { Token() });
            (await Sut().GetByUserAsync("u1", "tenant1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task RevokeByTokenIdAsync_Empty_ReturnsFalse()
        {
            Register();
            (await Sut().RevokeByTokenIdAsync("", "reason")).Should().BeFalse();
        }

        [Fact]
        public async Task RevokeByTokenIdAsync_Valid_ReturnsTrue()
        {
            Register();
            (await Sut().RevokeByTokenIdAsync("t1", "compromised")).Should().BeTrue();
        }

        [Fact]
        public async Task RevokeAllByTokenIdsAsync_NullOrEmpty_ReturnsZero()
        {
            Register();
            (await Sut().RevokeAllByTokenIdsAsync(null!, "r")).Should().Be(0);
            (await Sut().RevokeAllByTokenIdsAsync(new[] { " ", "" }, "r")).Should().Be(0);
        }

        [Fact]
        public async Task RevokeAllByTokenIdsAsync_Valid_ReturnsModifiedCount()
        {
            Register();
            (await Sut().RevokeAllByTokenIdsAsync(new[] { "t1", "t2" }, "r")).Should().Be(2);
        }

        [Fact]
        public async Task RevokeAllBySessionIdAsync_Empty_ReturnsZero()
        {
            Register();
            (await Sut().RevokeAllBySessionIdAsync("", "r")).Should().Be(0);
        }

        [Fact]
        public async Task RevokeAllBySessionIdAsync_Valid_ReturnsModifiedCount()
        {
            Register();
            (await Sut().RevokeAllBySessionIdAsync("s1", "logout")).Should().Be(2);
        }

        [Fact]
        public async Task UpdateSlidingExpiryAsync_UsesConfig_ReturnsTrue()
        {
            Register();
            _authRepo.Setup(a => a.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(new IdentityConfiguration { RefreshTokenValidForNumberMinutes = 60 });
            (await Sut().UpdateSlidingExpiryAsync("t1")).Should().BeTrue();
        }

        [Fact]
        public async Task UpdateSlidingExpiryAsync_NullConfig_UsesDefault()
        {
            Register();
            _authRepo.Setup(a => a.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);
            (await Sut().UpdateSlidingExpiryAsync("t1")).Should().BeTrue();
        }

        [Fact]
        public async Task DeleteAsync_ReturnsTrue()
        {
            Register();
            (await Sut().DeleteAsync("t1")).Should().BeTrue();
        }

        [Fact]
        public async Task GetExpiredAsync_ReturnsList()
        {
            Register(new[] { Token() });
            (await Sut().GetExpiredAsync()).Should().HaveCount(1);
        }
    }
}
