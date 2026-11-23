using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Moq;
using XUnitTest.TestSupport;
using static XUnitTest.IamTests.Batch.BatchHarness;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// Which users of an import get a mail, and with which template and organization: decided when the import
    /// finishes and saved on the job with it; the mail phase is then queued on its own queue.
    /// </summary>
    public class BatchMailPlanTests : IDisposable
    {
        private readonly BatchHarness _h = new();

        public BatchMailPlanTests()
        {
            AccessGrantTestKit.SignOut();
        }

        public void Dispose()
        {
            AccessGrantTestKit.SignOut();
            GC.SuppressFinalize(this);
        }

        private BatchJob Job => _h.Jobs.Jobs["job-1"];

        private static BatchJobItem WithMail(BatchJobItem item, string? template = null, string? existingUserTemplate = null, string? language = null)
        {
            item.Data.Mail = new BatchItemMail { Send = true, Template = template, ExistingUserTemplate = existingUserTemplate, Language = language };
            return item;
        }

        [Fact]
        public async Task EveryCase_IsPlannedOrSkippedWithItsCode_AndTheMailPhaseIsQueuedOnce()
        {
            _h.WithStoredUser("u-added", "added@example.com", new() { [OrgA] = ["viewer"] });
            _h.WithStoredUser("u-no-template", "notemplate@example.com", new() { [OrgA] = ["viewer"] });
            _h.WithStoredUser("u-same", "same@example.com", new() { [OrgA] = ["viewer"] });

            var job = _h.QueueJob("job-1",
                WithMail(Item(0, "new@example.com", Membership(OrgA, ["viewer"])), template: "Welcome"),
                WithMail(Item(1, "multi@example.com", Membership(OrgB, ["viewer"]), Membership(OrgA, ["viewer"])), template: "Welcome", language: "de-DE"),
                WithMail(Item(2, "notemplate.new@example.com", Membership(OrgA, ["viewer"]))),
                WithMail(Item(3, "added@example.com", Membership(OrgA, ["viewer"]), Membership(OrgB, ["viewer"])), template: "Welcome", existingUserTemplate: "MemberAdded"),
                WithMail(Item(4, "notemplate@example.com", Membership(OrgB, ["viewer"])), template: "Welcome"),
                WithMail(Item(5, "same@example.com", Membership(OrgA, ["editor"])), existingUserTemplate: "MemberAdded"),
                WithMail(Item(6, "failing@example.com", Membership("org-x")), template: "Welcome"),
                Item(7, "nomail@example.com", Membership(OrgA, ["viewer"])));

            await _h.ProcessAsync(job);

            Job.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            Job.Summary.Should().BeEquivalentTo(new BatchJobSummary { Created = 4, Updated = 3, Failed = 1 });

            var mail = Job.Mail!;
            mail.Status.Should().Be(BatchJobStatus.Queued);
            mail.Requested.Should().Be(7);
            mail.Skipped.Should().Be(4);
            mail.Results.Select(r => (r.Index, r.Outcome, r.Code)).Should().Equal(
                (2, BatchMailOutcomes.Skipped, BatchErrorCodes.TemplateRequired),
                (4, BatchMailOutcomes.Skipped, BatchErrorCodes.UserExists),
                (5, BatchMailOutcomes.Skipped, BatchErrorCodes.NoNewMembership),
                (6, BatchMailOutcomes.Skipped, BatchErrorCodes.ImportFailed));
            mail.Results.Should().OnlyContain(r => r.Message.Length > 0 && r.Ref == $"ref-{r.Index}");
            mail.Results.Single(r => r.Index == 5).Template.Should().Be("MemberAdded");

            var plan = Job.MailPlan!;
            plan.Select(e => (e.Index, e.Template, e.OrganizationId, e.Language)).Should().Equal(
                (0, "Welcome", OrgA, "en-US"),
                (1, "Welcome", OrgB, "de-DE"),
                (3, "MemberAdded", OrgB, "en-US"));
            plan[2].UserId.Should().Be("u-added");
            plan[2].Email.Should().Be("added@example.com");
            plan[0].OrganizationName.Should().Be(OrgA);

            var message = _h.PublishedMail.Should().ContainSingle().Subject;
            message.ConsumerName.Should().Be(IdpConstants.IamBatchMailQueue).And.Be("blocks_iam_listener_batch_mail");
            message.Payload.JobId.Should().Be("job-1");
            message.Payload.TenantId.Should().Be(BatchHarness.Tenant);
        }

        [Fact]
        public async Task NoItemAsksForAMail_LeavesNoMailPhase()
        {
            var item = Item(0, "a@example.com", Membership(OrgA, ["viewer"]));
            item.Data.Mail = new BatchItemMail { Send = false, Template = "Welcome" };

            await _h.ProcessAsync(_h.QueueJob("job-1", item));

            Job.Mail.Should().BeNull();
            Job.MailPlan.Should().BeNull();
            _h.PublishedMail.Should().BeEmpty();
        }

        [Fact]
        public async Task EveryRequestedMailSkipped_CompletesTheMailPhaseWithoutQueueingIt()
        {
            await _h.ProcessAsync(_h.QueueJob("job-1", WithMail(Item(0, "a@example.com", Membership(OrgA, ["viewer"])))));

            Job.Mail!.Status.Should().Be(BatchJobStatus.Completed);
            Job.Mail.CompletedAt.Should().NotBeNull();
            Job.Mail.Results.Should().ContainSingle().Which.Code.Should().Be(BatchErrorCodes.TemplateRequired);
            Job.MailPlan.Should().BeNull();
            _h.PublishedMail.Should().BeEmpty();
        }

        [Fact]
        public async Task DefaultOrganization_UsesTheDefaultOrganizationName()
        {
            _h.MultiOrg = false;

            await _h.ProcessAsync(_h.QueueJob("job-1", WithMail(Item(0, "a@example.com", Membership(null, ["base"])), template: "Welcome")));

            var entry = Job.MailPlan.Should().ContainSingle().Subject;
            entry.OrganizationId.Should().Be(IdpConstants.DefaultOrganizationId);
            entry.OrganizationName.Should().Be("Default");
        }

        [Fact]
        public async Task MailPhaseThatCannotBeQueued_FailsOnlyTheMailPhase()
        {
            _h.Messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BatchMailQueuedEvent>>()))
                .ThrowsAsync(new InvalidOperationException("broker down"));

            await _h.ProcessAsync(_h.QueueJob("job-1",
                WithMail(Item(0, "a@example.com", Membership(OrgA, ["viewer"])), template: "Welcome"),
                WithMail(Item(1, "b@example.com", Membership(OrgA, ["viewer"])))));

            Job.Status.Should().Be(BatchJobStatus.Completed);
            Job.Summary.Created.Should().Be(2);
            Job.Mail!.Status.Should().Be(BatchJobStatus.Failed);
            Job.Mail.Error.Should().Be(BatchErrorCodes.QueueUnavailable);
            Job.Mail.Results.Should().ContainSingle().Which.Code.Should().Be(BatchErrorCodes.TemplateRequired);
            Job.MailPlan.Should().BeNull();
        }

        [Fact]
        public async Task ErrorAfterTheWrite_StillSavesTheMailPhase()
        {
            _h.Users.Setup(u => u.BulkWriteUsersAsync(It.IsAny<IReadOnlyList<Blocks.Genesis.Entities.Iam.User>>(), It.IsAny<IReadOnlyList<Iam.DomainService.Users.UserBulkUpdate>>()))
                .Callback(() => _h.Jobs.FailHeartbeats = new InvalidOperationException("db blip"))
                .ReturnsAsync(_h.WriteResult);

            await _h.ProcessAsync(_h.QueueJob("job-1", WithMail(Item(0, "a@example.com", Membership(OrgA, ["viewer"])), template: "Welcome")));

            Job.Status.Should().Be(BatchJobStatus.Completed);
            Job.Mail!.Status.Should().Be(BatchJobStatus.Queued);
            Job.MailPlan.Should().ContainSingle();
            _h.PublishedMail.Should().ContainSingle();
        }
    }
}
