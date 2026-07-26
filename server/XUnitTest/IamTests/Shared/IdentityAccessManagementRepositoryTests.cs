using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// Unit tests for <see cref="IdentityAccessManagementRepository"/>. Collections resolve through the
    /// mocked <see cref="IDbContextProvider"/>; normalization, key-map activation guards and the
    /// activation-link resolution branches are exercised.
    /// </summary>
    public sealed class IdentityAccessManagementRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _db = new();

        private Mock<IMongoCollection<T>> Register<T>(IEnumerable<T>? items = null)
        {
            var col = MongoMock.Collection(items);
            _db.Setup(d => d.GetCollection<T>(It.IsAny<string>())).Returns(col.Object);
            _db.Setup(d => d.GetCollection<T>(It.IsAny<string>(), It.IsAny<string>())).Returns(col.Object);
            return col;
        }

        private IdentityAccessManagementRepository Sut() => new(_db.Object);

        [Fact]
        public async Task GetIamConfigurationAsync_ReturnsMatch()
        {
            var id = ObjectId.GenerateNewId();
            Register(new[] { new IamConfiguration { ItemId = id } });
            (await Sut().GetIamConfigurationAsync()).ItemId.Should().Be(id);
        }

        [Fact]
        public async Task GetUserByEmailAsync_ReturnsMatch()
        {
            Register(new[] { new User { ItemId = "u1", Email = "user@x.com" } });
            (await Sut().GetUserByEmailAsync("user@x.com")).ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUserByIdAsync_ReturnsMatch()
        {
            Register(new[] { new User { ItemId = "u1" } });
            (await Sut().GetUserByIdAsync("u1")).ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUserByIdAsyncGeneric_ReturnsProjected()
        {
            Register(new[] { new User { ItemId = "u1" } });
            (await Sut().GetUserByIdAsync<User>("u1")).ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_EmptyTenant_ReturnsFalse()
        {
            (await Sut().CheckPasswordBlackListedAsync("pw", " ")).Should().BeFalse();
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_Match_ReturnsTrue()
        {
            Register(new[] { new BlackListInformation { Key = "password", Value = "pw" } });
            (await Sut().CheckPasswordBlackListedAsync("pw", "tenant1")).Should().BeTrue();
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_NoMatch_ReturnsFalse()
        {
            var col = Register<BlackListInformation>();
            MongoMock.SetupCount(col, 0);
            (await Sut().CheckPasswordBlackListedAsync("pw", "tenant1")).Should().BeFalse();
        }

        [Fact]
        public async Task InsertUserKeyMapAsync_InsertsAndReturnsTrue()
        {
            var col = Register<UserKeyMap>();
            (await Sut().InsertUserKeyMapAsync(new UserKeyMap { ItemId = "k1" })).Should().BeTrue();
            col.Verify(c => c.InsertOneAsync(It.IsAny<UserKeyMap>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_NormalizesAndReplaces()
        {
            var col = Register<User>();
            var user = new User { ItemId = "u1", Email = " A@B.COM ", UserName = " Bob " };
            (await Sut().UpdateUserAsync(user)).Should().BeTrue();
            user.Email.Should().Be("a@b.com");
            user.UserName.Should().Be("bob");
            col.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<User>>(), It.IsAny<User>(),
                It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUserKeyMapActivationAsync_ReturnsAcknowledged()
        {
            Register<UserKeyMap>();
            (await Sut().UpdateUserKeyMapActivationAsync("u1")).Should().BeTrue();
        }

        [Fact]
        public async Task GetActiveUserKeyMapAsync_ReturnsList()
        {
            Register(new[] { new UserKeyMap { ItemId = "k1", UserId = "u1", Activated = false } });
            (await Sut().GetActiveUserKeyMapAsync("u1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetUserIdFromKeyMapByKeyAsync_NoKeyMap_ReturnsEmpty()
        {
            var col = Register<UserKeyMap>();
            MongoMock.SetupProjectedFind<UserKeyMap, string>(col, new List<string>());
            (await Sut().GetUserIdFromKeyMapByKeyAsync("key")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetUserIdFromKeyMapByKeyAsync_UserMissing_ReturnsEmpty()
        {
            var keyMap = Register<UserKeyMap>();
            MongoMock.SetupProjectedFind<UserKeyMap, string>(keyMap, new List<string> { "u1" });
            Register<User>();
            (await Sut().GetUserIdFromKeyMapByKeyAsync("key")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetUserIdFromKeyMapByKeyAsync_ActiveUser_ReturnsEmpty()
        {
            var keyMap = Register<UserKeyMap>();
            MongoMock.SetupProjectedFind<UserKeyMap, string>(keyMap, new List<string> { "u1" });
            Register(new[] { new User { ItemId = "u1", Active = true } });
            (await Sut().GetUserIdFromKeyMapByKeyAsync("key")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetUserIdFromKeyMapByKeyAsync_InactiveUser_ReturnsUserId()
        {
            var keyMap = Register<UserKeyMap>();
            MongoMock.SetupProjectedFind<UserKeyMap, string>(keyMap, new List<string> { "u1" });
            Register(new[] { new User { ItemId = "u1", Active = false } });
            (await Sut().GetUserIdFromKeyMapByKeyAsync("key")).Should().Be("u1");
        }

        [Fact]
        public async Task SaveSignUpSettingAsync_UpdatesOne()
        {
            var col = Register<TenantConfiguration>();
            await Sut().SaveSignUpSettingAsync(new TenantConfiguration { ItemId = "c1", IsEmailPasswordSignUpEnabled = true });
            col.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<TenantConfiguration>>(), It.IsAny<UpdateDefinition<TenantConfiguration>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetTenantConfigurationAsync_ReturnsMatch()
        {
            Register(new[] { new TenantConfiguration { ItemId = "c1" } });
            (await Sut().GetTenantConfigurationAsync()).ItemId.Should().Be("c1");
        }
    }
}
