using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Batch;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestDoubles;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// Against a real Mongo: the import's finish writes the mail phase and plan in the same update, the mail phase
    /// starts once and finishes conditionally, the template read sees the mail service's collection, and the bulk
    /// timeline insert is idempotent.
    /// </summary>
    public sealed class BatchMailRealMongoTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        private readonly RealMongo _mongo = new();
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        public BatchMailRealMongoTests()
        {
            _iam.Setup(r => r.GetCollection<BatchJob>(It.IsAny<string>())).Returns(_mongo.Database.GetCollection<BatchJob>("BatchJobs"));
        }

        public void Dispose() => _mongo.Dispose();

        private BatchJobRepository Jobs() => new(_iam.Object, new PassThroughIamDataAccess(), NullLogger<BatchJobRepository>.Instance);

        private async Task<BatchJobRepository> RunningJobAsync()
        {
            var jobs = Jobs();
            await jobs.InsertAsync(new BatchJob
            {
                ItemId = "j1",
                TenantId = Tenant,
                Status = BatchJobStatus.Queued,
                CreatedDate = DateTime.UtcNow,
                Items = [new BatchJobItem { Index = 0, Data = new BatchUserData { Email = "a@example.com" } }],
                Total = 1
            });
            await jobs.TryStartAsync("j1", Tenant, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-1));
            return jobs;
        }

        private static BatchJobFinish Finish(BatchJobMail? mail, List<BatchMailEntry>? plan) =>
            new(BatchJobStatus.Completed, new BatchJobSummary { Created = 1 }, [], null, DateTime.UtcNow, mail, plan);

        [RealMongoFact]
        public async Task Finish_WritesTheMailPhaseAndPlan_InTheSameUpdate_AndTheMailPhaseRunsOnce()
        {
            var jobs = await RunningJobAsync();
            var plan = new List<BatchMailEntry> { new() { Index = 0, Email = "a@example.com", UserId = "u1", OrganizationId = "org-a", Template = "Welcome" } };

            (await jobs.FinishAsync("j1", Tenant, Finish(new BatchJobMail { Requested = 1 }, plan), [BatchJobStatus.Running], 1)).Should().BeTrue();

            var stored = (await jobs.GetAsync("j1", Tenant))!;
            stored.Status.Should().Be(BatchJobStatus.Completed);
            stored.Items.Should().BeNull();
            stored.Mail!.Status.Should().Be(BatchJobStatus.Queued);
            stored.MailPlan.Should().ContainSingle().Which.Template.Should().Be("Welcome");

            var now = DateTime.UtcNow;
            var started = await jobs.TryStartMailAsync("j1", Tenant, now, now.AddMinutes(-5));
            started!.Mail!.Status.Should().Be(BatchJobStatus.Running);
            started.Mail.Attempt.Should().Be(1);
            (await jobs.TryStartMailAsync("j1", Tenant, now, now.AddMinutes(-5))).Should().BeNull("a live running phase is not started twice");

            var final = new BatchJobMail
            {
                Status = BatchJobStatus.Completed,
                Requested = 1,
                Queued = 1,
                Attempt = 1,
                CompletedAt = DateTime.UtcNow,
                Results = [new BatchMailResult { Index = 3, Code = "b" }, new BatchMailResult { Index = 1, Code = "a" }]
            };
            (await jobs.FinishMailAsync("j1", Tenant, final, [BatchJobStatus.Running], attempt: 2)).Should().BeFalse("another attempt owns it");
            (await jobs.FinishMailAsync("j1", Tenant, final, [BatchJobStatus.Running], attempt: 1)).Should().BeTrue();

            stored = (await jobs.GetAsync("j1", Tenant))!;
            stored.Mail!.Status.Should().Be(BatchJobStatus.Completed);
            stored.Mail.Results.Select(r => r.Index).Should().Equal(1, 3);
            stored.MailPlan.Should().BeNull();
            var raw = await _mongo.Database.GetCollection<BsonDocument>("BatchJobs").Find("{ _id: 'j1' }").FirstAsync();
            raw.Contains("MailPlan").Should().BeFalse();
        }

        [RealMongoFact]
        public async Task JobStoredBeforeThisChange_StillReads()
        {
            await _mongo.Database.GetCollection<BsonDocument>("BatchJobs").InsertOneAsync(BsonDocument.Parse("""
                { "_id": "old", "Resource": "users", "Status": "Running", "TenantId": "tenant-1", "Options": { "SendEmail": true },
                  "Items": [ { "Index": 0, "Ref": "r", "Operation": "upsert",
                               "Data": { "Email": "a@example.com", "SendEmail": true, "Memberships": [ { "OrganizationId": "org-a" } ] } } ],
                  "PendingSideEffects": [ { "UserId": "u1", "Kind": "MemberAdded", "OrganizationId": "org-a", "SendEmail": true } ],
                  "Total": 1, "Summary": { "Created": 0, "Updated": 0, "Unchanged": 0, "Failed": 0 }, "Failures": [], "Attempt": 1 }
                """));

            var job = (await Jobs().GetAsync("old", Tenant))!;

            job.Items.Should().ContainSingle().Which.Data.Email.Should().Be("a@example.com");
            job.PendingSideEffects.Should().ContainSingle().Which.Kind.Should().Be("MemberAdded");
            job.Mail.Should().BeNull();
        }

        [RealMongoFact]
        public async Task StaleRunningMailPhase_CanBeStartedAgain()
        {
            var jobs = await RunningJobAsync();
            await jobs.FinishAsync("j1", Tenant, Finish(new BatchJobMail { Requested = 1 }, [new BatchMailEntry { Template = "W" }]), [BatchJobStatus.Running], 1);
            var then = DateTime.UtcNow.AddMinutes(-10);
            await jobs.TryStartMailAsync("j1", Tenant, then, then.AddMinutes(-5));

            var now = DateTime.UtcNow;
            var restarted = await jobs.TryStartMailAsync("j1", Tenant, now, now.AddMinutes(-5));

            restarted!.Mail!.Attempt.Should().Be(2);
        }

        [RealMongoFact]
        public async Task FinishWithoutMail_LeavesNoMailFields()
        {
            var jobs = await RunningJobAsync();

            await jobs.FinishAsync("j1", Tenant, Finish(null, null), [BatchJobStatus.Running], 1);

            var raw = await _mongo.Database.GetCollection<BsonDocument>("BatchJobs").Find("{ _id: 'j1' }").FirstAsync();
            raw.Contains("Mail").Should().BeFalse();
            raw.Contains("MailPlan").Should().BeFalse();
        }

        [RealMongoFact]
        public async Task Templates_AreReadFromTheMailServiceCollection_ByNameAndLanguage()
        {
            await _mongo.Database.GetCollection<BsonDocument>(BatchMailTemplateRepository.CollectionName).InsertManyAsync(
            [
                new BsonDocument { ["_id"] = "t1", ["Name"] = "Welcome", ["Language"] = "en-US", ["OrganizationId"] = "org-a", ["TemplateBody"] = "<p>secret</p>" },
                new BsonDocument { ["_id"] = "t2", ["Name"] = "Welcome", ["Language"] = "en-US" },
                new BsonDocument { ["_id"] = "t3", ["Name"] = "Welcome", ["Language"] = "de-DE", ["OrganizationId"] = "" },
                new BsonDocument { ["_id"] = "t4", ["Name"] = "Other", ["Language"] = "en-US", ["OrganizationId"] = "default" }
            ]);

            var templates = await new BatchMailTemplateRepository(_mongo.ProviderFor()).GetAsync(Tenant, ["Welcome"], ["en-US", "de-DE"]);

            templates.Should().BeEquivalentTo(new[]
            {
                new BatchMailTemplate("Welcome", "en-US", "org-a"),
                new BatchMailTemplate("Welcome", "en-US", "default"),
                new BatchMailTemplate("Welcome", "de-DE", "default")
            });
            (await new BatchMailTemplateRepository(_mongo.ProviderFor()).GetAsync(Tenant, [], ["en-US"])).Should().BeEmpty();
        }

        [RealMongoFact]
        public async Task TimelineEntries_AreInsertedInBulk_AndARepeatIsSkipped()
        {
            var collection = _mongo.Database.GetCollection<UserActivity>("UserActivitys");
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<UserActivity>(
                Builders<UserActivity>.IndexKeys.Ascending(a => a.MessageId), new CreateIndexOptions { Unique = true }));
            var repository = new UserActivityRepository(_mongo.ProviderFor(), NullLogger<UserActivityRepository>.Instance, new PassThroughIamDataAccess());
            UserActivity Entry(string user) => new() { ItemId = Guid.NewGuid().ToString(), UserId = user, ActorUserId = user, Category = UserActivityCategory.Resource, Event = "USER_CREATED", MessageId = $"batch:j1:{user}" };

            await repository.InsertManyAsync([Entry("u1"), Entry("u2")], CancellationToken.None);
            await repository.InsertManyAsync([Entry("u2"), Entry("u3")], CancellationToken.None);
            await repository.InsertManyAsync([], CancellationToken.None);

            (await collection.CountDocumentsAsync(FilterDefinition<UserActivity>.Empty)).Should().Be(3);
        }
    }
}
