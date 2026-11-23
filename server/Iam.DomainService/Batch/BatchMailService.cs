using System.Collections.Concurrent;
using System.Diagnostics;
using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Batch
{
    public interface IBatchMailService
    {
        /// <summary>The mail phase of a job whose users are written: checks the templates and queues the mails.</summary>
        Task ProcessAsync(BatchMailQueuedEvent message);
    }

    /// <summary>
    /// The mail phase of a batch job, on its own queue after the import finished. It reads the job's mail plan,
    /// checks every template in one read with the mail service's own rule (the organization's template, else the
    /// Default organization's; same name and language), skips a mail whose template does not exist, and queues
    /// the others to the mail service in parallel. The template is not changed: the mail carries only the user's
    /// and organization's names, under the keys the organization mails already use.
    /// <para>
    /// At least once: a mail phase whose worker died is started again after <see cref="StaleAfter"/> and queues
    /// its mails again. Logs carry job ids, counts and indexes only.
    /// </para>
    /// </summary>
    public class BatchMailService : IBatchMailService
    {
        /// <summary>A running mail phase older than this is treated as abandoned by a dead worker.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

        /// <summary>Mails queued at the same time.</summary>
        public const int MaxParallelSends = 16;

        private static readonly string[] OpenStatuses = [BatchJobStatus.Queued, BatchJobStatus.Running];
        private static readonly string[] RunningStatus = [BatchJobStatus.Running];

        private readonly IBatchJobRepository _jobs;
        private readonly IBatchMailTemplateRepository _templates;
        private readonly IMessageClient _messageClient;
        private readonly ILogger<BatchMailService> _logger;

        public BatchMailService(
            IBatchJobRepository jobs,
            IBatchMailTemplateRepository templates,
            IMessageClient messageClient,
            ILogger<BatchMailService> logger)
        {
            _jobs = jobs;
            _templates = templates;
            _messageClient = messageClient;
            _logger = logger;
        }

        /// <summary>The clock the worker reads; replaceable in tests.</summary>
        public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

        public Task ProcessAsync(BatchMailQueuedEvent message)
        {
            ArgumentNullException.ThrowIfNull(message);
            return ProcessCoreAsync(message);
        }

        private async Task ProcessCoreAsync(BatchMailQueuedEvent message)
        {
            var tenantId = message.TenantId;
            var job = await _jobs.GetAsync(message.JobId, tenantId);
            if (job?.Mail is null)
            {
                _logger.LogWarning("Batch mail {JobId}: no job with a mail phase in tenant {TenantId}", message.JobId, tenantId);
                return;
            }

            if (!string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
            {
                _logger.LogWarning("Batch mail {JobId} belongs to another tenant than its message ({TenantId}); failed", job.ItemId, tenantId);
                await _jobs.FinishMailAsync(job.ItemId, tenantId, FailedMail(job.Mail, BatchErrorCodes.TenantMismatch), OpenStatuses);
                return;
            }

            var now = UtcNow();
            var started = await _jobs.TryStartMailAsync(job.ItemId, tenantId, now, now - StaleAfter);
            if (started?.Mail is null)
            {
                _logger.LogInformation("Batch mail {JobId} not started -- it is {Status}", job.ItemId, job.Mail.Status);
                return;
            }

            try
            {
                await RunAsync(started, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch mail {JobId} failed while processing", started.ItemId);
                await _jobs.FinishMailAsync(started.ItemId, tenantId, FailedMail(started.Mail, BatchErrorCodes.ProcessingFailed), RunningStatus, started.Mail.Attempt);
            }
        }

        private async Task RunAsync(BatchJob job, string tenantId)
        {
            var clock = Stopwatch.StartNew();
            var plan = job.MailPlan ?? [];
            var mail = job.Mail!;

            // A resumed phase keeps what planning decided and decides the template checks and sends again.
            var results = mail.Results.Where(r => BatchMailPlanner.PlanningCodes.Contains(r.Code)).ToList();

            var templates = await _templates.GetAsync(
                tenantId,
                plan.Select(e => e.Template).Distinct(StringComparer.Ordinal).ToList(),
                plan.Select(e => e.Language).Distinct(StringComparer.Ordinal).ToList());
            var lookupMs = clock.ElapsedMilliseconds;

            var toSend = new List<(BatchMailEntry Entry, string OrganizationId)>(plan.Count);
            foreach (var entry in plan)
            {
                var organization = BatchMailTemplates.Choose(templates, entry.Template, entry.Language, entry.OrganizationId);
                if (organization is null)
                {
                    results.Add(Result(entry, BatchMailOutcomes.Skipped, BatchErrorCodes.TemplateNotFound));
                }
                else
                {
                    toSend.Add((entry, organization));
                }
            }

            var failed = await SendAllAsync(job, toSend);
            results.AddRange(failed.Select(e => Result(e, BatchMailOutcomes.Failed, BatchErrorCodes.QueueError)));

            var final = new BatchJobMail
            {
                Requested = mail.Requested,
                Queued = toSend.Count - failed.Count,
                Skipped = results.Count(r => r.Outcome == BatchMailOutcomes.Skipped),
                Failed = results.Count(r => r.Outcome == BatchMailOutcomes.Failed),
                Results = results,
                Attempt = mail.Attempt,
                StartedAt = mail.StartedAt,
                CompletedAt = UtcNow()
            };
            final.Status = final.Failed == 0 ? BatchJobStatus.Completed : BatchJobStatus.CompletedWithErrors;

            if (await _jobs.FinishMailAsync(job.ItemId, tenantId, final, RunningStatus, mail.Attempt))
            {
                _logger.LogInformation(
                    "Batch mail {JobId} finished -- {Status}: {Queued} queued, {Skipped} skipped, {Failed} failed; template lookup {LookupMs} ms, total {TotalMs} ms",
                    job.ItemId, final.Status, final.Queued, final.Skipped, final.Failed, lookupMs, clock.ElapsedMilliseconds);
            }
        }

        /// <summary>Queues the mails, <see cref="MaxParallelSends"/> at a time. Returns the ones that could not be queued.</summary>
        private async Task<List<BatchMailEntry>> SendAllAsync(BatchJob job, List<(BatchMailEntry Entry, string OrganizationId)> mails)
        {
            var failed = new ConcurrentBag<BatchMailEntry>();
            using var gate = new SemaphoreSlim(MaxParallelSends);
            await Task.WhenAll(mails.Select(async mail =>
            {
                await gate.WaitAsync();
                try
                {
                    await _messageClient.SendToConsumerAsync(new ConsumerMessage<SendMail>
                    {
                        ConsumerName = IdpConstants.MailQueue,
                        Payload = Mail(job, mail.Entry, mail.OrganizationId)
                    });
                }
                catch (Exception ex)
                {
                    // The type only: the message could quote the recipient.
                    _logger.LogWarning("Batch mail {JobId}: mail of item {Index} could not be queued ({ExceptionType})", job.ItemId, mail.Entry.Index, ex.GetType().Name); // NOSONAR S6667 - type only, by design
                    failed.Add(mail.Entry);
                }
                finally
                {
                    gate.Release();
                }
            }));

            return failed.OrderBy(e => e.Index).ToList();
        }

        /// <summary>The mail as the mail service takes it. The template is used as stored: no link, no generated text.</summary>
        private static SendMail Mail(BatchJob job, BatchMailEntry entry, string organizationId)
        {
            var displayName = $"{entry.FirstName} {entry.LastName}".Trim();
            return new SendMail
            {
                To = [entry.Email],
                Purpose = entry.Template,
                Language = entry.Language,
                OrganizationId = organizationId,
                CorrelationId = $"{job.ItemId}:{entry.Index}",
                BodyDataContext = new Dictionary<string, string>
                {
                    ["DisplayName"] = displayName.Length == 0 ? entry.Email : displayName,
                    ["FirstName"] = entry.FirstName ?? string.Empty,
                    ["LastName"] = entry.LastName ?? string.Empty,
                    ["UserName"] = entry.Email,
                    ["Email"] = entry.Email,
                    ["OrganizationId"] = entry.OrganizationId,
                    ["OrganizationName"] = entry.OrganizationName ?? entry.OrganizationId
                }
            };
        }

        private static BatchMailResult Result(BatchMailEntry entry, string outcome, string code) => new()
        {
            Ref = entry.Ref,
            Index = entry.Index,
            Email = entry.Email,
            Outcome = outcome,
            Code = code,
            Message = BatchErrorCodes.MessageFor(code),
            Template = entry.Template,
            Language = entry.Language
        };

        private BatchJobMail FailedMail(BatchJobMail mail, string error) => new()
        {
            Status = BatchJobStatus.Failed,
            Requested = mail.Requested,
            Skipped = mail.Skipped,
            Results = mail.Results,
            Error = error,
            Attempt = mail.Attempt,
            StartedAt = mail.StartedAt,
            CompletedAt = UtcNow()
        };
    }
}
