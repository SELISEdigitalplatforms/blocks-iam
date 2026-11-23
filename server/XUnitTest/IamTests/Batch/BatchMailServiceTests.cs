using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Moq;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>
    /// The mail phase of a batch job: one template read, the mail service's template rule (the organization's
    /// own, else the Default organization's; same name and language), skipped mails for missing templates, and
    /// the mails queued to the mail service in parallel.
    /// </summary>
    public class BatchMailServiceTests
    {
        private const string Tenant = "tenant-1";
        private const string OrgA = "org-a";
        private const string OrgB = "org-b";

        private readonly InMemoryBatchJobRepository _jobs = new();
        private readonly Mock<IBatchMailTemplateRepository> _templates = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly CapturingLogger<BatchMailService> _logger = new();
        private readonly List<SendMail> _sent = [];
        private readonly List<BatchMailTemplate> _stored = [];
        private DateTime _now = new(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

        public BatchMailServiceTests()
        {
            _templates.Setup(t => t.GetAsync(Tenant, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<string>>()))
                .ReturnsAsync((string _, IReadOnlyCollection<string> names, IReadOnlyCollection<string> languages) =>
                    _stored.Where(t => names.Contains(t.Name) && languages.Contains(t.Language)).ToList());
            _messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(m =>
                {
                    m.ConsumerName.Should().Be(IdpConstants.MailQueue);
                    lock (_sent)
                    {
                        _sent.Add(m.Payload);
                    }
                })
                .Returns(Task.CompletedTask);
        }

        private BatchMailService Service() => new(_jobs, _templates.Object, _messages.Object, _logger) { UtcNow = () => _now };

        private BatchMailServiceTests Template(string name, string language, string organizationId)
        {
            _stored.Add(new BatchMailTemplate(name, language, organizationId));
            return this;
        }

        private static BatchMailEntry Entry(int index, string template, string organizationId, string language = "en-US") => new()
        {
            Index = index,
            Ref = $"ref-{index}",
            Email = $"u{index}@example.com",
            UserId = $"u{index}",
            OrganizationId = organizationId,
            Template = template,
            Language = language,
            FirstName = "Ann",
            LastName = "Lee",
            OrganizationName = organizationId == "default" ? "Default" : $"{organizationId} name"
        };

        private BatchJob Job(params BatchMailEntry[] entries)
        {
            var job = new BatchJob
            {
                ItemId = "job-1",
                TenantId = Tenant,
                Status = BatchJobStatus.Completed,
                Mail = new BatchJobMail
                {
                    Status = BatchJobStatus.Queued,
                    Requested = entries.Length + 1,
                    Skipped = 1,
                    Results = [new BatchMailResult { Index = 99, Email = "x@example.com", Code = BatchErrorCodes.UserExists }]
                },
                MailPlan = entries.ToList()
            };
            _jobs.Jobs[job.ItemId] = job;
            return job;
        }

        private Task RunAsync(string tenant = Tenant) =>
            Service().ProcessAsync(new BatchMailQueuedEvent { JobId = "job-1", TenantId = tenant });

        [Fact]
        public async Task TemplateChoice_OrganizationFirst_ThenDefault_ElseSkipped()
        {
            Template("Welcome", "en-US", OrgA).Template("Welcome", "en-US", "default").Template("Invite", "en-US", "default");
            var job = Job(
                Entry(0, "Welcome", OrgA),
                Entry(1, "Welcome", OrgB),
                Entry(2, "Invite", OrgA),
                Entry(3, "Missing", OrgA),
                Entry(4, "Invite", OrgB, language: "de-DE"));

            await RunAsync();

            _sent.Select(m => (m.To.Single(), m.Purpose, m.OrganizationId)).Should().BeEquivalentTo(new[]
            {
                ("u0@example.com", "Welcome", OrgA),
                ("u1@example.com", "Welcome", "default"),
                ("u2@example.com", "Invite", "default")
            });

            var mail = job.Mail!;
            mail.Status.Should().Be(BatchJobStatus.Completed);
            mail.Queued.Should().Be(3);
            mail.Skipped.Should().Be(3);
            mail.Failed.Should().Be(0);
            mail.Results.Select(r => (r.Index, r.Code)).Should().Equal(
                (3, BatchErrorCodes.TemplateNotFound), (4, BatchErrorCodes.TemplateNotFound), (99, BatchErrorCodes.UserExists));
            mail.Results[0].Template.Should().Be("Missing");
            mail.Results[1].Language.Should().Be("de-DE");
            job.MailPlan.Should().BeNull();
            _templates.Verify(t => t.GetAsync(Tenant, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
        }

        [Fact]
        public async Task Mail_CarriesTheTemplateAsIs_WithOnlyNamesAndOrganization()
        {
            Template("Welcome", "fr-FR", OrgA);
            Job(Entry(0, "Welcome", OrgA, language: "fr-FR"));

            await RunAsync();

            var sent = _sent.Should().ContainSingle().Subject;
            sent.Language.Should().Be("fr-FR");
            sent.CorrelationId.Should().Be("job-1:0");
            sent.BodyDataContext.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["DisplayName"] = "Ann Lee",
                ["FirstName"] = "Ann",
                ["LastName"] = "Lee",
                ["UserName"] = "u0@example.com",
                ["Email"] = "u0@example.com",
                ["OrganizationId"] = OrgA,
                ["OrganizationName"] = "org-a name"
            });
            sent.BodyDataContext.Keys.Should().NotContain(k => k.Contains("Link", StringComparison.OrdinalIgnoreCase) || k.Contains("Url", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task MailThatCannotBeQueued_FailsAlone()
        {
            Template("Welcome", "en-US", "default");
            _messages.Setup(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<SendMail>>(c => c.Payload.To.Single() == "u1@example.com")))
                .ThrowsAsync(new InvalidOperationException("bus down u1@example.com"));
            var job = Job(Entry(0, "Welcome", OrgA), Entry(1, "Welcome", OrgA), Entry(2, "Welcome", OrgA));

            await RunAsync();

            job.Mail!.Status.Should().Be(BatchJobStatus.CompletedWithErrors);
            job.Mail.Queued.Should().Be(2);
            job.Mail.Failed.Should().Be(1);
            job.Mail.Results.Should().Contain(r => r.Index == 1 && r.Outcome == BatchMailOutcomes.Failed && r.Code == BatchErrorCodes.QueueError);
            string.Join("\n", _logger.Messages).Should().NotContain("@");
        }

        [Fact]
        public async Task ManyMails_AreQueuedInParallel_UpToTheLimit()
        {
            Template("Welcome", "en-US", "default");
            var running = 0;
            var peak = 0;
            _messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Returns(async () =>
                {
                    var now = Interlocked.Increment(ref running);
                    int current;
                    while ((current = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, current) != current)
                    {
                    }

                    await Task.Delay(20);
                    Interlocked.Decrement(ref running);
                });
            var job = Job(Enumerable.Range(0, 50).Select(i => Entry(i, "Welcome", OrgA)).ToArray());

            await RunAsync();

            job.Mail!.Queued.Should().Be(50);
            peak.Should().BeGreaterThan(1).And.BeLessThanOrEqualTo(BatchMailService.MaxParallelSends);
        }

        [Fact]
        public async Task RedeliveredMessage_OfARunningOrFinishedPhase_SendsNothingAgain()
        {
            Template("Welcome", "en-US", "default");
            var job = Job(Entry(0, "Welcome", OrgA));

            await RunAsync();
            await RunAsync();

            _sent.Should().ContainSingle();
            job.Mail!.Attempt.Should().Be(1);
        }

        [Fact]
        public async Task StaleRunningPhase_IsStartedAgain_KeepingThePlanningSkips()
        {
            Template("Welcome", "en-US", "default");
            var job = Job(Entry(0, "Welcome", OrgA));
            job.Mail!.Status = BatchJobStatus.Running;
            job.Mail.Attempt = 1;
            job.Mail.StartedAt = _now - BatchMailService.StaleAfter - TimeSpan.FromSeconds(1);
            job.Mail.Results.Add(new BatchMailResult { Index = 0, Code = BatchErrorCodes.QueueError, Outcome = BatchMailOutcomes.Failed });

            await RunAsync();

            _sent.Should().ContainSingle();
            job.Mail!.Attempt.Should().Be(2);
            job.Mail.Results.Select(r => r.Code).Should().Equal(BatchErrorCodes.UserExists);
            job.Mail.Queued.Should().Be(1);
        }

        [Fact]
        public async Task LiveRunningPhase_IsLeftAlone()
        {
            var job = Job(Entry(0, "Welcome", OrgA));
            job.Mail!.Status = BatchJobStatus.Running;
            job.Mail.StartedAt = _now.AddSeconds(-10);

            await RunAsync();

            _sent.Should().BeEmpty();
            job.Mail.Status.Should().Be(BatchJobStatus.Running);
        }

        [Fact]
        public async Task TenantMismatch_FailsTheMailPhase_AndSendsNothing()
        {
            var job = Job(Entry(0, "Welcome", OrgA));

            await RunAsync("other-tenant");

            job.Mail!.Status.Should().Be(BatchJobStatus.Failed);
            job.Mail.Error.Should().Be(BatchErrorCodes.TenantMismatch);
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task TemplateReadError_FailsTheMailPhase()
        {
            _templates.Setup(t => t.GetAsync(Tenant, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<string>>()))
                .ThrowsAsync(new InvalidOperationException("db down"));
            var job = Job(Entry(0, "Welcome", OrgA));

            await RunAsync();

            job.Mail!.Status.Should().Be(BatchJobStatus.Failed);
            job.Mail.Error.Should().Be(BatchErrorCodes.ProcessingFailed);
            job.Mail.Results.Should().ContainSingle(r => r.Code == BatchErrorCodes.UserExists);
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task JobWithoutMailPhase_IsIgnored()
        {
            _jobs.Jobs["job-1"] = new BatchJob { ItemId = "job-1", TenantId = Tenant, Status = BatchJobStatus.Completed };

            await RunAsync();

            _templates.VerifyNoOtherCalls();
            _sent.Should().BeEmpty();
        }

        [Fact]
        public void Choose_MatchesNameAndLanguageExactly_AndTreatsAnEmptyOrganizationAsDefault()
        {
            var templates = new List<BatchMailTemplate>
            {
                new("Welcome", "en-US", BatchMailTemplates.NormalizeOrganization("")),
                new("Welcome", "de-DE", OrgA)
            };

            BatchMailTemplates.Choose(templates, "Welcome", "en-US", OrgA).Should().Be("default");
            BatchMailTemplates.Choose(templates, "Welcome", "de-DE", OrgA).Should().Be(OrgA);
            BatchMailTemplates.Choose(templates, "welcome", "en-US", OrgA).Should().BeNull();
            BatchMailTemplates.Choose(templates, "Welcome", "en-us", OrgA).Should().BeNull();
            BatchMailTemplates.Choose(templates, "Welcome", "de-DE", OrgB).Should().BeNull();
            BatchMailTemplates.Choose(templates, "Welcome", "en-US", "").Should().Be("default");
        }
    }
}
