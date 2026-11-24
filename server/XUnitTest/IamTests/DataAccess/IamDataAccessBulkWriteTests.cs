using Blocks.Genesis.DataAccess.Rules;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Services;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.DataAccess
{
    /// <summary>
    /// The governed bulk write: every operation passes the same gates as its single-document helper, and one
    /// that is denied or breaks a rule fails alone while the others are written.
    /// </summary>
    public sealed class IamDataAccessBulkWriteTests : IDisposable
    {
        private readonly RealMongo _mongo = new();
        private readonly Mock<IIamDataAccess> _access = new();

        public IamDataAccessBulkWriteTests()
        {
            _access.Setup(a => a.InsertAllowedAsync(IamSchemas.User, It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(true);
            _access.Setup(a => a.PrepareWriteAsync(IamSchemas.User, It.IsAny<User>(), true, It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync((string _, User u, bool _, string _, string? _) => u);
            _access.Setup(a => a.EditFilterAsync(IamSchemas.User, It.IsAny<FilterDefinition<User>>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync((string _, FilterDefinition<User> f, string _, string? _) => f);
        }

        public void Dispose() => _mongo.Dispose();

        private IMongoCollection<User> Users => _mongo.Database.GetCollection<User>("Users");

        private static (FilterDefinition<User>, UpdateDefinition<User>) SetName(string id, string name) =>
            (Builders<User>.Filter.Eq(x => x.ItemId, id), Builders<User>.Update.Set(x => x.FirstName, name));

        private static DataValidationException RuleFailed()
        {
            var result = new DataValidationResult();
            result.AddError("FirstName", "too long", "MaxLength");
            return new DataValidationException(result);
        }

        [RealMongoFact]
        public async Task DeniedOrInvalidOperations_FailAlone_AndTheOthersAreWritten()
        {
            await Users.InsertManyAsync([new User { ItemId = "e1" }, new User { ItemId = "e2" }, new User { ItemId = "e3" }]);
            _access.Setup(a => a.PrepareWriteAsync(IamSchemas.User, It.Is<User>(u => u.ItemId == "bad-insert"), true, It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(RuleFailed());
            _access.Setup(a => a.EditFilterAsync(IamSchemas.User, It.IsAny<FilterDefinition<User>>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync((string _, FilterDefinition<User> f, string _, string? _) =>
                    f.Render(new RenderArgs<User>(MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<User>(), MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString().Contains("e2") ? null : f);
            _access.Setup(a => a.CheckUpdateAsync(IamSchemas.User, It.IsAny<UpdateDefinition<User>>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Returns((string _, UpdateDefinition<User> u, string _, string? _) =>
                    u.Render(new RenderArgs<User>(MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<User>(), MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString().Contains("invalid")
                        ? throw RuleFailed()
                        : Task.CompletedTask);

            var outcome = await _access.Object.BulkWriteAsync(
                Users, IamSchemas.User,
                [new User { ItemId = "ok-insert" }, new User { ItemId = "bad-insert" }, new User { ItemId = "e1" }],
                [SetName("e1", "One"), SetName("e2", "Two"), SetName("e3", "invalid")],
                "test");

            outcome.Failed.Should().BeEquivalentTo([1, 4, 5]);
            outcome.DuplicateKeys.Should().BeEquivalentTo([2]);
            (await Users.CountDocumentsAsync(x => x.ItemId == "ok-insert")).Should().Be(1);
            (await Users.CountDocumentsAsync(x => x.ItemId == "bad-insert")).Should().Be(0);
            (await Users.Find(x => x.ItemId == "e1").FirstAsync()).FirstName.Should().Be("One");
            (await Users.Find(x => x.ItemId == "e2").FirstAsync()).FirstName.Should().BeNull();
        }

        [RealMongoFact]
        public async Task InsertGateDenied_FailsEveryInsert_ButUpdatesStillRun()
        {
            await Users.InsertOneAsync(new User { ItemId = "e1" });
            _access.Setup(a => a.InsertAllowedAsync(IamSchemas.User, It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(false);

            var outcome = await _access.Object.BulkWriteAsync(Users, IamSchemas.User, [new User { ItemId = "n1" }, new User { ItemId = "n2" }], [SetName("e1", "One")], "test");

            outcome.Failed.Should().BeEquivalentTo([0, 1]);
            (await Users.CountDocumentsAsync(FilterDefinition<User>.Empty)).Should().Be(1);
            (await Users.Find(x => x.ItemId == "e1").FirstAsync()).FirstName.Should().Be("One");
        }

        [Fact]
        public async Task NothingToWrite_DoesNotTouchTheCollection()
        {
            var collection = new Mock<IMongoCollection<User>>(MockBehavior.Strict);

            var outcome = await _access.Object.BulkWriteAsync(collection.Object, IamSchemas.User, [], [], "test");

            outcome.Failed.Should().BeEmpty();
            outcome.DuplicateKeys.Should().BeEmpty();
        }
    }
}
