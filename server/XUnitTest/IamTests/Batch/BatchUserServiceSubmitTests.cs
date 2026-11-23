using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;
using Iam.DomainService.Notifications;
using Iam.DomainService.Utilities;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>The API side of the batch upsert: POST iam/batch/users and GET iam/batch/jobs/{jobId} (spec 683 §6.2, §6.3).</summary>
    public class BatchUserServiceSubmitTests : IDisposable
    {
        private readonly BatchHarness _h = new();

        public BatchUserServiceSubmitTests()
        {
            AccessGrantTestKit.SignIn(BatchHarness.Caller, "default");
        }

        public void Dispose()
        {
            AccessGrantTestKit.SignOut();
            GC.SuppressFinalize(this);
        }

        private static BatchUserItem Item(string? reference, string email) => new()
        {
            Ref = reference,
            Email = email,
            Memberships = [new BatchMembership { OrganizationId = BatchHarness.OrgA }]
        };

        [Fact]
        public async Task ValidItems_SaveOneQueuedJob_AndPublishOnlyItsId_H1()
        {
            var request = new BatchUsersRequest
            {
                ClientReference = "migration-007",
                Items = [Item("u1", "A@Example.com"), Item(null, "bad")]
            };

            var response = await _h.Service().SubmitAsync(request);

            response.IsSuccess.Should().BeTrue();
            response.Status.Should().Be(BatchJobStatus.Queued);
            response.Total.Should().Be(2);
            response.Rejected.Should().Be(1);
            response.Errors.Should().BeNull();

            var job = _h.Jobs.Jobs.Values.Should().ContainSingle().Subject;
            job.ItemId.Should().Be(response.JobId);
            job.Status.Should().Be(BatchJobStatus.Queued);
            job.TenantId.Should().Be(BatchHarness.Tenant);
            job.Resource.Should().Be(BatchResources.Users);
            job.ClientReference.Should().Be("migration-007");
            job.Items.Should().ContainSingle().Which.Data.Email.Should().Be("a@example.com");
            job.Items![0].Ref.Should().Be("u1");
            job.Failures.Should().ContainSingle().Which.Index.Should().Be(1);
            job.Failures[0].Ref.Should().BeNull();
            job.Summary.Failed.Should().Be(1);
            job.RequestedBy.Should().Be(BatchHarness.Caller);
            job.RequestedByOrganizationId.Should().Be("default");
            job.RequestedBySystemCaller.Should().BeTrue();
            job.RequestedByClientId.Should().BeNull();

            var message = _h.Published.Should().ContainSingle().Subject;
            message.ConsumerName.Should().Be(IdpConstants.IamBatchQueue).And.Be("blocks_iam_listener_batch");
            message.Payload.JobId.Should().Be(job.ItemId);
            message.Payload.TenantId.Should().Be(BatchHarness.Tenant);
            _h.SentNotifications.Should().BeEmpty();
        }

        [Fact]
        public async Task RequestLevelError_SavesAndQueuesNothing_C1()
        {
            var response = await _h.Service().SubmitAsync(new BatchUsersRequest { Items = [Item("u1", "a@x.com"), Item("u2", "A@X.com")] });

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey(BatchErrorCodes.DuplicateEmail);
            _h.Jobs.Jobs.Should().BeEmpty();
            _h.Published.Should().BeEmpty();
        }

        [Fact]
        public async Task EveryItemRejected_SavesACompletedWithErrorsJob_WithoutQueueMessage_C4()
        {
            var response = await _h.Service().SubmitAsync(new BatchUsersRequest { Items = [Item("u1", "not-an-email"), Item("u2", "x")] });

            response.IsSuccess.Should().BeTrue();
            response.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            response.Total.Should().Be(2);
            response.Rejected.Should().Be(2);
            var job = _h.Jobs.Jobs.Values.Single();
            job.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            job.CompletedAt.Should().NotBeNull();
            job.Items.Should().BeNull();
            job.Failures.Should().HaveCount(2).And.OnlyContain(f => f.Errors.Single().Code == BatchErrorCodes.EmailInvalid);
            _h.Published.Should().BeEmpty();
            _h.SentNotifications.Should().ContainSingle().Which.ConfigurationName.Should().Be(BuiltInNotificationNames.BatchJobFinished);
        }

        [Fact]
        public async Task QueueFailure_FailsTheJob_AndAnswersWithErrorsQueue_C12()
        {
            _h.Messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<BatchJobQueuedEvent>>()))
                .ThrowsAsync(new InvalidOperationException("broker down"));

            var response = await _h.Service().SubmitAsync(new BatchUsersRequest { Items = [Item("u1", "a@example.com")] });

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("Queue").WhoseValue.Should().Be("The batch could not be queued. Try again.");
            var job = _h.Jobs.Jobs.Values.Single();
            job.Status.Should().Be(BatchJobStatus.Failed);
            job.Error.Should().Be(BatchErrorCodes.QueueUnavailable);
            job.Items.Should().BeNull();
        }

        [Fact]
        public async Task OrganizationCaller_IsRecordedAsNotSystem()
        {
            AccessGrantTestKit.SignIn("org-admin", BatchHarness.OrgA);

            await _h.Service().SubmitAsync(new BatchUsersRequest { Items = [Item("u1", "a@example.com")] });

            var job = _h.Jobs.Jobs.Values.Single();
            job.RequestedBy.Should().Be("org-admin");
            job.RequestedByOrganizationId.Should().Be(BatchHarness.OrgA);
            job.RequestedBySystemCaller.Should().BeFalse();
            job.CreatedBy.Should().Be("org-admin");
        }

        [Theory]
        [InlineData(BatchHarness.OrgA, false)]
        [InlineData("default", true)]
        public async Task ClientCaller_IsRecordedWithItsClientId(string organizationId, bool system)
        {
            AccessGrantTestKit.SignInClient("client-1", organizationId);

            await _h.Service().SubmitAsync(new BatchUsersRequest { Items = [Item("u1", "a@example.com")] });

            var job = _h.Jobs.Jobs.Values.Single();
            job.RequestedBy.Should().BeNull();
            job.RequestedByClientId.Should().Be("client-1");
            job.RequestedBySystemCaller.Should().Be(system);
        }

        [Fact]
        public async Task UnsupportedFields_AreIgnoredAndOnlyTheirNamesLogged()
        {
            var item = JsonSerializer.Deserialize<BatchUserItem>(
                """{"ref":"u1","email":"a@example.com","password":"Sup3rSecret!","active":true,"memberships":[{"organizationId":"org-a"}]}""",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            await _h.Service().SubmitAsync(new BatchUsersRequest { Items = [item] });

            _h.Logger.Messages.Should().Contain(m => m.Contains("Batch item 0 carried 2 unsupported field(s)", StringComparison.Ordinal));
            string.Join("\n", _h.Logger.Messages).Should().NotContain("Sup3rSecret!").And.NotContain("a@example.com").And.NotContain("password");
        }

        [Fact]
        public async Task GetJob_ReturnsTheTenantsJob_WithFailuresOrderedByIndex_H13()
        {
            var job = _h.QueueJob("job-1");
            job.Status = BatchJobStatus.CompletedWithErrors;
            job.ClientReference = "ref";
            job.Total = 3;
            job.Summary = new BatchJobSummary { Created = 1, Failed = 2 };
            job.Failures =
            [
                new BatchItemFailure { Ref = "c", Index = 2, Email = "c@example.com" },
                new BatchItemFailure { Ref = "a", Index = 0, Email = "a@example.com" }
            ];

            var response = await _h.Service().GetJobAsync("job-1");

            response.Should().NotBeNull();
            response!.IsSuccess.Should().BeTrue();
            response.JobId.Should().Be("job-1");
            response.Resource.Should().Be(BatchResources.Users);
            response.ClientReference.Should().Be("ref");
            response.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            response.Total.Should().Be(3);
            response.Summary.Created.Should().Be(1);
            response.Failures.Select(f => f.Index).Should().Equal(0, 2);
        }

        [Fact]
        public async Task GetJob_UnknownOrOtherTenantJob_IsNotFound_C18()
        {
            var foreign = _h.QueueJob("job-foreign");
            foreign.TenantId = "other-tenant";

            (await _h.Service().GetJobAsync("missing")).Should().BeNull();
            (await _h.Service().GetJobAsync("job-foreign")).Should().BeNull();
            (await _h.Service().GetJobAsync("")).Should().BeNull();
        }

        [Fact]
        public async Task GetJob_WithoutTenantContext_IsNotFound()
        {
            _h.QueueJob("job-1");
            BlocksContext.SetContext(null!);

            (await _h.Service().GetJobAsync("job-1")).Should().BeNull();
        }
    }
}
