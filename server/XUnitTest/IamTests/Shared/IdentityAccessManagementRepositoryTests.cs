using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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
        private const string TestConnectionString = "mongodb://localhost:27017";
        private const string TestRootDatabaseName = "BlocksRootDb";

        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IBlocksSecret> _secret = new();

        public IdentityAccessManagementRepositoryTests()
        {
            _secret.SetupGet(s => s.DatabaseConnectionString).Returns(TestConnectionString);
            _secret.SetupGet(s => s.RootDatabaseName).Returns(TestRootDatabaseName);
        }

        private Mock<IMongoCollection<T>> Register<T>(IEnumerable<T>? items = null)
        {
            var col = MongoMock.Collection(items);
            _db.Setup(d => d.GetCollection<T>(It.IsAny<string>())).Returns(col.Object);
            _db.Setup(d => d.GetCollection<T>(It.IsAny<string>(), It.IsAny<string>())).Returns(col.Object);
            return col;
        }

        /// <summary>
        /// The blacklist lives in the root database, reached through GetDatabase rather than the
        /// tenant-scoped GetCollection the other fixtures use.
        /// </summary>
        private Mock<IMongoCollection<T>> RegisterBlackList<T>(IEnumerable<T>? items = null)
        {
            var col = MongoMock.Collection(items);
            var database = MongoMock.Database();
            MongoMock.OnDatabase(database, "BlackListInformations", col);
            _db.Setup(d => d.GetDatabase(TestConnectionString, TestRootDatabaseName, false))
                .Returns(database.Object);
            return col;
        }

        private IdentityAccessManagementRepository Sut() =>
            new(_db.Object, _secret.Object, NullLogger<IdentityAccessManagementRepository>.Instance);

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

        // The former CheckPasswordBlackListedAsync_EmptyTenant_ReturnsFalse test has been removed
        // rather than adapted. It asserted that a blank tenant id short-circuits to "not
        // blacklisted", which is exactly the fail-open behaviour this change eliminates; keeping it
        // green would have required reinstating the vulnerability. The replacement is
        // CheckPasswordBlackListedAsync_NoTenantContext_StillChecks below.

        [Fact]
        public async Task CheckPasswordBlackListedAsync_Match_ReturnsTrue()
        {
            RegisterBlackList(new[] { new BlackListInformation { Key = "password", Value = "pw" } });
            (await Sut().CheckPasswordBlackListedAsync("pw")).Should().BeTrue();
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_NoMatch_ReturnsFalse()
        {
            var col = RegisterBlackList<BlackListInformation>();
            MongoMock.SetupCount(col, 0);
            (await Sut().CheckPasswordBlackListedAsync("pw")).Should().BeFalse();
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_ResolvesTheRootDatabase()
        {
            RegisterBlackList(new[] { new BlackListInformation { Key = "password", Value = "pw" } });

            await Sut().CheckPasswordBlackListedAsync("pw");

            // The whole point of the fix: the lookup goes to the root database named by the secret,
            // never to a tenant database, so one entry blocks the password for every tenant.
            _db.Verify(d => d.GetDatabase(TestConnectionString, TestRootDatabaseName, false), Times.Once);
            _db.Verify(d => d.GetCollection<BlackListInformation>(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_NoTenantContext_StillChecks()
        {
            BlocksContext.SetContext(null);
            RegisterBlackList(new[] { new BlackListInformation { Key = "password", Value = "pw" } });

            // Without a tenant the old code returned false outright, leaving every context-free
            // path unprotected.
            (await Sut().CheckPasswordBlackListedAsync("pw")).Should().BeTrue();
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_RootDatabaseUnreachable_Propagates()
        {
            var col = RegisterBlackList<BlackListInformation>();
            col.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BlackListInformation>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("root database unreachable"));

            // Fail closed: an outage must fail the request, not report the password as safe.
            await Assert.ThrowsAsync<TimeoutException>(() => Sut().CheckPasswordBlackListedAsync("pw"));
        }

        /// <summary>
        /// MongoMock.SetupIndexes returns void, so build the index manager here where the test needs
        /// to assert against it.
        /// </summary>
        private static Mock<IMongoIndexManager<BlackListInformation>> RegisterIndexes(
            Mock<IMongoCollection<BlackListInformation>> col)
        {
            var indexes = new Mock<IMongoIndexManager<BlackListInformation>>();
            indexes.Setup(i => i.CreateOneAsync(
                    It.IsAny<CreateIndexModel<BlackListInformation>>(),
                    It.IsAny<CreateOneIndexOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("ix");
            col.Setup(c => c.Indexes).Returns(indexes.Object);
            return indexes;
        }

        [Fact]
        public async Task EnsureIndexesAsync_CreatesTheCompoundIndex()
        {
            var indexes = RegisterIndexes(RegisterBlackList<BlackListInformation>());

            await Sut().EnsureIndexesAsync();

            indexes.Verify(i => i.CreateOneAsync(
                It.Is<CreateIndexModel<BlackListInformation>>(m =>
                    m.Options.Name == "ix_key_value" && RendersAscendingKeyThenValue(m)),
                It.IsAny<CreateOneIndexOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The name alone would still pass if the keys were wrong or reversed, so render the
        /// definition and check the actual ordered fields.
        /// </summary>
        private static bool RendersAscendingKeyThenValue(CreateIndexModel<BlackListInformation> model)
        {
            var registry = BsonSerializer.SerializerRegistry;
            var rendered = model.Keys.Render(
                new RenderArgs<BlackListInformation>(registry.GetSerializer<BlackListInformation>(), registry));

            return rendered.ElementCount == 2
                && rendered.GetElement(0).Name == "Key" && rendered.GetElement(0).Value == 1
                && rendered.GetElement(1).Name == "Value" && rendered.GetElement(1).Value == 1;
        }

        [Fact]
        public async Task EnsureIndexesAsync_SwallowsFailuresAndWarns()
        {
            var indexes = RegisterIndexes(RegisterBlackList<BlackListInformation>());
            indexes.Setup(i => i.CreateOneAsync(
                    It.IsAny<CreateIndexModel<BlackListInformation>>(),
                    It.IsAny<CreateOneIndexOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("no permission"));

            var logger = new Mock<ILogger<IdentityAccessManagementRepository>>();
            var sut = new IdentityAccessManagementRepository(_db.Object, _secret.Object, logger.Object);

            // A missing index costs speed, never correctness, so startup must survive this - but it
            // must not vanish silently either, or a permanently missing index goes unnoticed.
            await sut.EnsureIndexesAsync();

            logger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
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

