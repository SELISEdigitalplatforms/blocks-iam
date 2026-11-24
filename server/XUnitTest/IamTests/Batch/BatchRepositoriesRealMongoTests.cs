using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Batch;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestDoubles;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// The batch job store and the user bulk methods against a real Mongo: the unique partial index that keeps
    /// one job running per tenant, the conditional start and finish, and the unordered bulk write.
    /// </summary>
    public sealed class BatchRepositoriesRealMongoTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        private readonly RealMongo _mongo = new();
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        public BatchRepositoriesRealMongoTests()
        {
            _iam.Setup(r => r.GetCollection<BatchJob>(It.IsAny<string>())).Returns(_mongo.Database.GetCollection<BatchJob>("BatchJobs"));
            _iam.Setup(r => r.GetCollection<User>()).Returns(_mongo.Database.GetCollection<User>("Users"));
        }

        public void Dispose() => _mongo.Dispose();

        private BatchJobRepository Jobs() => new(_iam.Object, new PassThroughIamDataAccess(), NullLogger<BatchJobRepository>.Instance);

        private UserRepository Users() => new(_iam.Object, new PassThroughIamDataAccess());

        private static BatchJob NewJob(string id, DateTime? created = null) => new()
        {
            ItemId = id,
            TenantId = Tenant,
            Status = BatchJobStatus.Queued,
            CreatedDate = created ?? DateTime.UtcNow,
            Items = [new BatchJobItem { Index = 0, Ref = "r", Data = new BatchUserData { Email = "a@example.com" } }],
            Total = 1
        };

        [RealMongoFact]
        public async Task OnlyOneJobRunsPerResource_TheSecondStartIsAConflict()
        {
            var jobs = Jobs();
            await jobs.InsertAsync(NewJob("j1"));
            await jobs.InsertAsync(NewJob("j2"));
            var now = DateTime.UtcNow;

            var first = await jobs.TryStartAsync("j1", Tenant, now, now.AddSeconds(-60));
            var second = await jobs.TryStartAsync("j2", Tenant, now, now.AddSeconds(-60));

            first.Job!.Status.Should().Be(BatchJobStatus.Running);
            first.Job.Attempt.Should().Be(1);
            first.Job.StartedAt.Should().NotBeNull();
            second.Should().Be(new BatchJobStart(null, true));
            (await jobs.GetAsync("j2", Tenant))!.Status.Should().Be(BatchJobStatus.Queued);
            (await jobs.GetRunningAsync(BatchResources.Users, Tenant))!.ItemId.Should().Be("j1");
        }

        [RealMongoFact]
        public async Task FinishedJob_IsNotStartedAgain()
        {
            var jobs = Jobs();
            await jobs.InsertAsync(NewJob("j1"));
            var now = DateTime.UtcNow;
            await jobs.TryStartAsync("j1", Tenant, now, now.AddSeconds(-60));
            await jobs.FinishAsync("j1", Tenant, new BatchJobFinish(BatchJobStatus.Completed, new BatchJobSummary { Created = 1 }, [], null, now), [BatchJobStatus.Running]);

            (await jobs.TryStartAsync("j1", Tenant, now, now.AddSeconds(-60))).Should().Be(new BatchJobStart(null, false));
        }

        [RealMongoFact]
        public async Task StaleRunningJob_CanBeRequeued_OnlyWithItsOldHeartbeat_AndThenResumed()
        {
            var jobs = Jobs();
            await jobs.InsertAsync(NewJob("j1"));
            var old = DateTime.UtcNow.AddMinutes(-5);
            var started = (await jobs.TryStartAsync("j1", Tenant, old, old.AddSeconds(-60))).Job!;

            (await jobs.RequeueStaleAsync("j1", Tenant, old.AddSeconds(1))).Should().BeFalse();

            // A stale running job is resumed directly by its own redelivered message...
            var now = DateTime.UtcNow;
            var resumed = (await jobs.TryStartAsync("j1", Tenant, now, now.AddSeconds(-60))).Job!;
            resumed.Attempt.Should().Be(2);
            resumed.StartedAt.Should().BeCloseTo(started.StartedAt!.Value, TimeSpan.FromMilliseconds(5));

            // ...or put back in the queue by another job's worker, conditional on the heartbeat it saw.
            (await jobs.RequeueStaleAsync("j1", Tenant, resumed.HeartbeatAt)).Should().BeTrue();
            (await jobs.GetAsync("j1", Tenant))!.Status.Should().Be(BatchJobStatus.Queued);
        }

        [RealMongoFact]
        public async Task Finish_RemovesItems_AndOnlyTheFirstCallMovesTheJob()
        {
            var jobs = Jobs();
            await jobs.InsertAsync(NewJob("j1"));
            var now = DateTime.UtcNow;
            await jobs.TryStartAsync("j1", Tenant, now, now.AddSeconds(-60));
            await jobs.SetPendingSideEffectsAsync("j1", Tenant, [new BatchPendingSideEffect { UserId = "u", Kind = BatchSideEffectKinds.Created, OrganizationId = "o" }], now);
            await jobs.HeartbeatAsync("j1", Tenant, now.AddSeconds(1));
            var failures = new List<BatchItemFailure> { new() { Ref = "b", Index = 3 }, new() { Ref = "a", Index = 1 } };
            var finish = new BatchJobFinish(BatchJobStatus.CompletedWithErrors, new BatchJobSummary { Created = 1, Failed = 2 }, failures, null, now);

            (await jobs.FinishAsync("j1", Tenant, finish, [BatchJobStatus.Running])).Should().BeTrue();
            (await jobs.FinishAsync("j1", Tenant, finish, [BatchJobStatus.Running])).Should().BeFalse();

            var stored = (await jobs.GetAsync("j1", Tenant))!;
            stored.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            stored.Items.Should().BeNull();
            stored.PendingSideEffects.Should().BeEmpty();
            stored.Failures.Select(f => f.Index).Should().Equal(1, 3);
            stored.Summary.Failed.Should().Be(2);
            stored.CompletedAt.Should().NotBeNull();
            var raw = await _mongo.Database.GetCollection<MongoDB.Bson.BsonDocument>("BatchJobs").Find("{ _id: 'j1' }").FirstAsync();
            raw.Contains("Items").Should().BeFalse();
        }

        [RealMongoFact]
        public async Task WritesOfAnOlderAttempt_DoNotTouchTheCurrentAttempt()
        {
            var jobs = Jobs();
            await jobs.InsertAsync(NewJob("j1"));
            var old = DateTime.UtcNow.AddMinutes(-5);
            await jobs.TryStartAsync("j1", Tenant, old, old.AddSeconds(-60));
            var now = DateTime.UtcNow;
            await jobs.TryStartAsync("j1", Tenant, now, now.AddSeconds(-60));
            await jobs.SetPendingSideEffectsAsync("j1", Tenant, [new BatchPendingSideEffect { UserId = "current" }], now, attempt: 2);

            await jobs.SetPendingSideEffectsAsync("j1", Tenant, [], now, attempt: 1);
            await jobs.HeartbeatAsync("j1", Tenant, now.AddMinutes(1), attempt: 1);
            var finished = await jobs.FinishAsync("j1", Tenant,
                new BatchJobFinish(BatchJobStatus.Completed, new BatchJobSummary(), [], null, now), [BatchJobStatus.Running], attempt: 1);

            finished.Should().BeFalse();
            var stored = (await jobs.GetAsync("j1", Tenant))!;
            stored.Status.Should().Be(BatchJobStatus.Running);
            stored.PendingSideEffects.Should().ContainSingle(s => s.UserId == "current");
            stored.HeartbeatAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        }

        [RealMongoFact]
        public async Task ItemAttributes_RoundTripAsPlainValues()
        {
            var jobs = Jobs();
            var job = NewJob("j1");
            job.Items![0].Data.Attributes = new Dictionary<string, object>
            {
                ["legacyUserId"] = "123",
                ["access"] = new Dictionary<string, object> { ["org-a"] = new Dictionary<string, object> { ["region"] = new List<object> { "eu" } } }
            };
            await jobs.InsertAsync(job);

            var raw = await _mongo.Database.GetCollection<MongoDB.Bson.BsonDocument>("BatchJobs").Find("{ _id: 'j1' }").FirstAsync();
            raw.ToString().Should().NotContain("_t").And.Contain("\"region\" : [\"eu\"]");
            var stored = (await jobs.GetAsync("j1", Tenant))!.Items![0].Data.Attributes!;
            stored["legacyUserId"].Should().Be("123");
            Iam.DomainService.AccessGrant.AccessAttributes.Read(stored)["org-a"]["region"].Should().BeEquivalentTo(["eu"]);
        }

        [RealMongoFact]
        public async Task OldestQueued_IsReturnedFirst()
        {
            var jobs = Jobs();
            await jobs.InsertAsync(NewJob("late", DateTime.UtcNow));
            await jobs.InsertAsync(NewJob("early", DateTime.UtcNow.AddMinutes(-1)));

            (await jobs.GetOldestQueuedAsync(BatchResources.Users, Tenant))!.ItemId.Should().Be("early");
            (await jobs.GetOldestQueuedAsync(BatchResources.Users, "other-tenant")).Should().BeNull();
        }

        [RealMongoFact]
        public async Task Users_AreReadByEmailsAndIds_InOneQueryEach()
        {
            var collection = _mongo.Database.GetCollection<User>("Users");
            await collection.InsertManyAsync(
            [
                new User { ItemId = "u1", Email = "a@example.com" },
                new User { ItemId = "u2", Email = "b@example.com" },
                new User { ItemId = "u3", Email = "c@example.com" }
            ]);

            (await Users().GetUsersByEmailsAsync(["A@Example.com ", "b@example.com", "missing@example.com", ""]))
                .Select(u => u.ItemId).Should().BeEquivalentTo(["u1", "u2"]);
            (await Users().GetUsersByIdsAsync(["u3", "u1", " "])).Select(u => u.ItemId).Should().BeEquivalentTo(["u1", "u3"]);
            (await Users().GetUsersByEmailsAsync([])).Should().BeEmpty();
            (await Users().GetUsersByIdsAsync([])).Should().BeEmpty();
        }

        [Fact]
        public async Task UsersByEmails_DeniedRead_FailsClosed()
        {
            var access = new Mock<IIamDataAccess>();
            access.Setup(a => a.ReadAsync(IamSchemas.User, It.IsAny<FilterDefinition<User>>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()))
                .ReturnsAsync(new ReadScope<User>(false, FilterDefinition<User>.Empty, null));

            var act = () => new UserRepository(_iam.Object, access.Object).GetUsersByEmailsAsync(["a@example.com"]);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [RealMongoFact]
        public async Task BulkWrite_WritesEveryUser_AndReportsOnlyTheOneThatFailed()
        {
            var collection = _mongo.Database.GetCollection<User>("Users");
            await collection.InsertOneAsync(new User
            {
                ItemId = "existing",
                Email = "e@example.com",
                OrganizationIds = ["org-a"],
                Roles = new() { ["org-a"] = ["viewer"] },
                Permissions = new() { ["org-a"] = [] }
            });

            var update = Builders<User>.Update.Combine(
                Builders<User>.Update.AddToSet(x => x.OrganizationIds, "org-b"),
                Builders<User>.Update.AddToSetEach("Roles.org-a", new[] { "viewer", "editor" }),
                Builders<User>.Update.Set("Roles.org-b", new List<string> { "editor" }));

            var result = await Users().BulkWriteUsersAsync(
                [
                    new User { ItemId = "new-1", Email = " New@Example.com " },
                    new User { ItemId = "existing", Email = "dup@example.com" }
                ],
                [new UserBulkUpdate("existing", update)]);

            result.DuplicateUserIds.Should().BeEquivalentTo(["existing"]);
            result.FailedUserIds.Should().BeEmpty();
            (await collection.Find(x => x.ItemId == "new-1").FirstAsync()).Email.Should().Be("new@example.com");
            var stored = await collection.Find(x => x.ItemId == "existing").FirstAsync();
            stored.OrganizationIds.Should().Equal("org-a", "org-b");
            stored.Roles["org-a"].Should().Equal("viewer", "editor");
            stored.Roles["org-b"].Should().Equal("editor");
        }
    }
}
