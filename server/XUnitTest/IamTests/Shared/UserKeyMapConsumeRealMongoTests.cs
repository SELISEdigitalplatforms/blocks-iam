using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// <see cref="IdentityAccessManagementRepository.TryConsumeUserKeyMapAsync"/> against a real local Mongo:
    /// the single-use guarantee is the server's conditional update, which a mock cannot show.
    /// </summary>
    public sealed class UserKeyMapConsumeRealMongoTests : IDisposable
    {
        private readonly RealMongo _mongo = new();

        public void Dispose() => _mongo.Dispose();

        private IdentityAccessManagementRepository Sut()
        {
            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns(RealMongo.ConnectionString);
            secret.SetupGet(s => s.RootDatabaseName).Returns(_mongo.Database.DatabaseNamespace.DatabaseName);
            return new IdentityAccessManagementRepository(
                _mongo.ProviderFor(),
                secret.Object,
                NullLogger<IdentityAccessManagementRepository>.Instance,
                new XUnitTest.TestDoubles.PassThroughIamDataAccess());
        }

        private static UserKeyMap Row(string key, string userId, DateTime expireDate, bool activated = false) => new()
        {
            ItemId = Guid.NewGuid().ToString(),
            Key = key,
            UserId = userId,
            IssueDate = DateTime.Now,
            ExpireDate = expireDate,
            MailPurpose = "RecoverAccount",
            Activated = activated
        };

        [RealMongoFact]
        public async Task FirstConsume_Succeeds_AndASecondIsRefused()
        {
            var repo = Sut();
            await repo.InsertUserKeyMapAsync(Row("code-1", "u1", DateTime.Now.AddMinutes(30)));

            (await repo.TryConsumeUserKeyMapAsync("code-1", "u1", DateTime.UtcNow)).Should().BeTrue();
            (await repo.TryConsumeUserKeyMapAsync("code-1", "u1", DateTime.UtcNow)).Should().BeFalse();
            (await repo.GetUserKeyMapByKeyAsync("code-1"))!.Activated.Should().BeTrue();
        }

        [RealMongoFact]
        public async Task ManyConsumesAtOnce_ExactlyOneWins()
        {
            var repo = Sut();
            await repo.InsertUserKeyMapAsync(Row("code-race", "u1", DateTime.Now.AddMinutes(30)));

            var attempts = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() => repo.TryConsumeUserKeyMapAsync("code-race", "u1", DateTime.UtcNow)));
            var results = await Task.WhenAll(attempts);

            results.Count(r => r).Should().Be(1);
        }

        [RealMongoFact]
        public async Task ExpiredCode_IsRefused_AndStaysUnused()
        {
            var repo = Sut();
            await repo.InsertUserKeyMapAsync(Row("code-old", "u1", DateTime.Now.AddSeconds(-1)));

            (await repo.TryConsumeUserKeyMapAsync("code-old", "u1", DateTime.UtcNow)).Should().BeFalse();
            (await repo.GetUserKeyMapByKeyAsync("code-old"))!.Activated.Should().BeFalse();
        }

        [RealMongoFact]
        public async Task CodeAlreadyInvalidated_IsRefused()
        {
            var repo = Sut();
            await repo.InsertUserKeyMapAsync(Row("code-done", "u1", DateTime.Now.AddMinutes(30), activated: true));

            (await repo.TryConsumeUserKeyMapAsync("code-done", "u1", DateTime.UtcNow)).Should().BeFalse();
        }

        [RealMongoFact]
        public async Task AnotherUsersCode_OrAnUnknownCode_IsRefused()
        {
            var repo = Sut();
            await repo.InsertUserKeyMapAsync(Row("code-a", "alice", DateTime.Now.AddMinutes(30)));

            (await repo.TryConsumeUserKeyMapAsync("code-a", "bob", DateTime.UtcNow)).Should().BeFalse();
            (await repo.TryConsumeUserKeyMapAsync("no-such-code", "alice", DateTime.UtcNow)).Should().BeFalse();
            (await repo.TryConsumeUserKeyMapAsync("code-a", "alice", DateTime.UtcNow)).Should().BeTrue();
        }

        [Theory]
        [InlineData("", "u1")]
        [InlineData("code", "")]
        [InlineData(" ", " ")]
        public async Task BlankKeyOrUser_IsRefused_WithoutTouchingTheStore(string key, string userId)
        {
            var db = new Mock<IDbContextProvider>(MockBehavior.Strict);
            var repo = new IdentityAccessManagementRepository(
                db.Object,
                Mock.Of<IBlocksSecret>(),
                NullLogger<IdentityAccessManagementRepository>.Instance,
                new XUnitTest.TestDoubles.PassThroughIamDataAccess());

            (await repo.TryConsumeUserKeyMapAsync(key, userId, DateTime.UtcNow)).Should().BeFalse();
        }
    }
}
