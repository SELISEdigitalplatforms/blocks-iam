using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Dtos;
using Iam.DomainService.Notifications;
using Iam.DomainService.Resources;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Batch
{
    /// <summary>The user and organization stores the batch reads and writes; one dependency for the service.</summary>
    public sealed class BatchUserStores
    {
        public BatchUserStores(IUserRepository users, IResourceRepository resources)
        {
            Users = users;
            Resources = resources;
        }

        public IUserRepository Users { get; }
        public IResourceRepository Resources { get; }
    }

    public interface IBatchUserService
    {
        /// <summary>POST iam/batch/users: checks the request, saves the job and queues it.</summary>
        Task<BatchSubmitResponse> SubmitAsync(BatchUsersRequest? request);

        /// <summary>GET iam/batch/jobs/{jobId}: a job of the caller's tenant, or null.</summary>
        Task<BatchJobResponse?> GetJobAsync(string jobId);

        /// <summary>The worker: runs the job, then every job of the tenant still queued behind it.</summary>
        Task ProcessAsync(BatchJobQueuedEvent message);
    }

    /// <summary>
    /// Async batch user upsert (iam/batch/users). The API checks the request, stores the accepted items on a
    /// job and queues only its id. The worker loads everything the job needs in bulk, validates every item in
    /// memory, writes all users in one bulk write and stores a summary with the failed users.
    /// <para>
    /// The worker never trusts its message context for authorization: the submitter recorded on the job is
    /// rebuilt when the job runs. Logs carry job ids, tenants, counts and item indexes, never emails, names,
    /// phone numbers or attribute values.
    /// </para>
    /// </summary>
    public class BatchUserService : IBatchUserService
    {
        /// <summary>A running job whose heartbeat is older than this is treated as abandoned by a dead worker.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

        /// <summary>How often a job that cannot start yet looks again while it waits for the running one.</summary>
        public static readonly TimeSpan StartPollInterval = TimeSpan.FromSeconds(5);

        /// <summary>Side effects sent between two heartbeats, so a long side-effect phase does not look abandoned.</summary>
        private const int SideEffectsPerHeartbeat = 10;

        private static readonly string[] OpenStatuses = [BatchJobStatus.Queued, BatchJobStatus.Running];
        private static readonly string[] RunningStatus = [BatchJobStatus.Running];

        private readonly IBatchJobRepository _jobs;
        private readonly IUserRepository _users;
        private readonly IResourceRepository _resources;
        private readonly IAccessGrantService _accessGrant;
        private readonly IUserManagementMutationService _userMutations;
        private readonly IMessageClient _messageClient;
        private readonly IBuiltInNotificationSender? _notificationSender;
        private readonly ILogger<BatchUserService> _logger;
        private readonly BatchUserPlanner _planner;

        public BatchUserService(
            IBatchJobRepository jobs,
            BatchUserStores stores,
            IAccessGrantService accessGrant,
            IUserManagementMutationService userMutations,
            IMessageClient messageClient,
            ILogger<BatchUserService> logger,
            IBuiltInNotificationSender? notificationSender = null)
        {
            ArgumentNullException.ThrowIfNull(stores);
            _jobs = jobs;
            _users = stores.Users;
            _resources = stores.Resources;
            _accessGrant = accessGrant;
            _userMutations = userMutations;
            _messageClient = messageClient;
            _logger = logger;
            _notificationSender = notificationSender;
            _planner = new BatchUserPlanner(accessGrant, userMutations.MapUser);
        }

        /// <summary>The clock the worker reads; replaceable in tests.</summary>
        public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

        /// <summary>The wait between start attempts; replaceable in tests.</summary>
        public Func<TimeSpan, Task> Delay { get; init; } = interval => Task.Delay(interval);

        #region Submit and read

        public async Task<BatchSubmitResponse> SubmitAsync(BatchUsersRequest? request)
        {
            var validation = BatchRequestValidator.Validate(request);
            if (validation.RequestErrors is not null)
            {
                _logger.LogInformation("Batch submit refused -- {Codes}", string.Join(", ", validation.RequestErrors.Keys));
                return new BatchSubmitResponse { Errors = validation.RequestErrors };
            }

            WarnOnUnmappedFields(request!.Items!);
            var job = NewJob(request, validation);
            await _jobs.InsertAsync(job);

            if (validation.Accepted.Count == 0)
            {
                _logger.LogInformation("Batch job {JobId} saved with every item rejected ({Rejected})", job.ItemId, job.Summary.Failed);
                await NotifyFinishedAsync(job, job.Status, job.Summary);
                return Accepted(job);
            }

            try
            {
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<BatchJobQueuedEvent>
                {
                    ConsumerName = IdpConstants.IamBatchQueue,
                    Payload = new BatchJobQueuedEvent { JobId = job.ItemId, TenantId = job.TenantId }
                });
            }
            catch (Exception ex)
            {
                // A job nobody will run must not stay Queued: it is failed, and the caller is told to try again.
                _logger.LogError(ex, "Batch job {JobId} could not be queued", job.ItemId);
                await FailAsync(job, job.TenantId, BatchErrorCodes.QueueUnavailable, [BatchJobStatus.Queued]);
                return new BatchSubmitResponse
                {
                    Errors = new Dictionary<string, string> { { "Queue", "The batch could not be queued. Try again." } }
                };
            }

            _logger.LogInformation(
                "Batch job {JobId} queued -- {Total} items, {Rejected} rejected", job.ItemId, job.Total, job.Summary.Failed);
            return Accepted(job);
        }

        public async Task<BatchJobResponse?> GetJobAsync(string jobId)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(tenantId))
            {
                return null;
            }

            var job = await _jobs.GetAsync(jobId, tenantId);
            return job is not null && string.Equals(job.TenantId, tenantId, StringComparison.Ordinal)
                ? BatchJobResponse.From(job)
                : null;
        }

        private BatchJob NewJob(BatchUsersRequest request, BatchValidation validation)
        {
            var context = BlocksContext.GetContext();
            var caller = _accessGrant.CurrentCaller();
            var userId = string.IsNullOrWhiteSpace(context?.UserId) ? null : context.UserId;

            // CurrentCaller drops the client id of a default-organization client; it is kept here so the
            // worker can read that client's own permissions.
            var clientId = userId is null && !string.IsNullOrWhiteSpace(context?.ClientId) ? context.ClientId : null;
            var now = DateTime.UtcNow;
            var allRejected = validation.Accepted.Count == 0;

            return new BatchJob
            {
                ItemId = Guid.NewGuid().ToString(),
                Resource = BatchResources.Users,
                Status = allRejected ? BatchJobStatus.CompletedWithErrors : BatchJobStatus.Queued,
                TenantId = context?.TenantId ?? string.Empty,
                ClientReference = request.ClientReference,
                RequestedBy = userId,
                RequestedByOrganizationId = caller.OrganizationId,
                RequestedBySystemCaller = caller.IsSystem,
                RequestedByClientId = clientId,
                Options = new BatchJobOptions { SendEmail = request.Options?.SendEmail ?? false },
                Items = allRejected ? null : validation.Accepted,
                Total = request.Items!.Count,
                Summary = new BatchJobSummary { Failed = validation.Rejected.Count },
                Failures = validation.Rejected,
                CreatedDate = now,
                LastUpdatedDate = now,
                CreatedBy = userId ?? clientId,
                LastUpdatedBy = userId ?? clientId,
                CompletedAt = allRejected ? now : null
            };
        }

        private static BatchSubmitResponse Accepted(BatchJob job) => new()
        {
            IsSuccess = true,
            JobId = job.ItemId,
            Status = job.Status,
            Total = job.Total,
            Rejected = job.Summary.Failed
        };

        /// <summary>Fields outside the contract are ignored; how many an item carried is logged, never their names or values.</summary>
        private void WarnOnUnmappedFields(List<BatchUserItem> items)
        {
            for (var index = 0; index < items.Count; index++)
            {
                var unmapped = items[index]?.Data?.UnmappedFields;
                if (unmapped is { Count: > 0 })
                {
                    // A count only: the names are caller-controlled text.
                    _logger.LogWarning("Batch item {Index} carried {Count} unsupported field(s); ignored", index, unmapped.Count);
                }
            }
        }

        #endregion

        #region Worker

        public Task ProcessAsync(BatchJobQueuedEvent message)
        {
            ArgumentNullException.ThrowIfNull(message);
            return ProcessCoreAsync(message);
        }

        private async Task ProcessCoreAsync(BatchJobQueuedEvent message)
        {
            var tenantId = message.TenantId;
            var job = await _jobs.GetAsync(message.JobId, tenantId);
            if (job is null)
            {
                _logger.LogWarning("Batch job {JobId} not found in tenant {TenantId}", message.JobId, tenantId);
                return;
            }

            if (!string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
            {
                _logger.LogWarning("Batch job {JobId} belongs to another tenant than its message ({TenantId}); failed", job.ItemId, tenantId);
                await FailAsync(job, tenantId, BatchErrorCodes.TenantMismatch, OpenStatuses);
                return;
            }

            // One job of a tenant runs at a time. The worker that holds the running slot drains the tenant's
            // queued jobs, so a job refused here is not lost: it runs after the current one.
            var next = job;
            while (next is not null)
            {
                var running = await StartAsync(next, tenantId);
                if (running is null)
                {
                    return;
                }

                await RunAsync(running, tenantId);
                next = await _jobs.GetOldestQueuedAsync(BatchResources.Users, tenantId);
            }
        }

        /// <summary>
        /// Starts the job (§6.5 step 2). A job that cannot start yet does not give up at once: its message is the only
        /// thing that would run it again. It waits, for a bounded time, while the job itself or another job of the
        /// tenant is running with a live heartbeat; a job whose worker stopped sending heartbeats is resumed (its own)
        /// or put back in the queue (another). Null when the job is final, still owned by a live worker, or the wait
        /// ran out; a live owner finishes it and drains the queue behind it.
        /// </summary>
        private async Task<BatchJob?> StartAsync(BatchJob job, string tenantId)
        {
            var deadline = UtcNow() + StaleAfter + StartPollInterval + StartPollInterval;
            while (true)
            {
                var now = UtcNow();
                var staleBefore = now - StaleAfter;
                var start = await _jobs.TryStartAsync(job.ItemId, tenantId, now, staleBefore);
                if (start.Job is not null)
                {
                    return start.Job;
                }

                if (!await MayStillStartAsync(job, tenantId, start.Conflict, staleBefore))
                {
                    return null;
                }

                if (UtcNow() >= deadline)
                {
                    _logger.LogInformation("Batch job {JobId} not started -- another worker still holds the tenant's running job", job.ItemId);
                    return null;
                }

                await Delay(StartPollInterval);
            }
        }

        /// <summary>
        /// Why the start failed, and whether trying again can help: false when the job is gone or final. A running job
        /// of the tenant whose heartbeat is stale goes back to the queue, conditional on the heartbeat that was read.
        /// </summary>
        private async Task<bool> MayStillStartAsync(BatchJob job, string tenantId, bool conflict, DateTime staleBefore)
        {
            if (!conflict)
            {
                var current = await _jobs.GetAsync(job.ItemId, tenantId);
                if (current is null || BatchJobStatus.IsFinal(current.Status))
                {
                    _logger.LogInformation("Batch job {JobId} not started -- it is {Status}", job.ItemId, current?.Status ?? "missing");
                    return false;
                }

                return true;
            }

            var running = await _jobs.GetRunningAsync(job.Resource, tenantId);
            if (running is not null && running.HeartbeatAt < staleBefore
                && await _jobs.RequeueStaleAsync(running.ItemId, tenantId, running.HeartbeatAt))
            {
                _logger.LogWarning("Batch job {StaleJobId} had no heartbeat since {HeartbeatAt}; requeued", running.ItemId, running.HeartbeatAt);
            }

            return true;
        }

        private async Task RunAsync(BatchJob job, string tenantId)
        {
            _logger.LogInformation("Batch job {JobId} started -- attempt {Attempt}, {Count} items", job.ItemId, job.Attempt, job.Items?.Count ?? 0);
            WriteResult? result = null;
            try
            {
                if (job.PendingSideEffects.Count > 0)
                {
                    await ReplaySideEffectsAsync(job, tenantId);
                }

                var plans = await PlanAsync(job, tenantId);
                var written = await WriteAsync(job, tenantId, plans);
                result = written.Result;

                await SendSideEffectsAsync(job, tenantId, written.SideEffects, written.UsersById);
                await _jobs.SetPendingSideEffectsAsync(job.ItemId, tenantId, [], UtcNow(), job.Attempt);
                await FinishAsync(job, tenantId, result);
            }
            catch (Exception ex) when (result is null)
            {
                _logger.LogError(ex, "Batch job {JobId} failed while processing", job.ItemId);
                await FailAsync(job, tenantId, BatchErrorCodes.ProcessingFailed, RunningStatus, job.Attempt);
            }
            catch (Exception ex)
            {
                // The users are written: the job ends with what was written, not as failed.
                _logger.LogError(ex, "Batch job {JobId} failed after its users were written; finishing with the write result", job.ItemId);
                await FinishAsync(job, tenantId, result!);
            }
        }

        /// <summary>A resumed job first sends what its previous attempt planned and may not have sent (at least once).</summary>
        private async Task ReplaySideEffectsAsync(BatchJob job, string tenantId)
        {
            _logger.LogInformation("Batch job {JobId} resumes {Count} pending side effects", job.ItemId, job.PendingSideEffects.Count);
            var users = await _users.GetUsersByIdsAsync(job.PendingSideEffects.Select(s => s.UserId).ToList());
            await SendSideEffectsAsync(job, tenantId, job.PendingSideEffects, users.ToDictionary(u => u.ItemId, StringComparer.Ordinal));
            await _jobs.SetPendingSideEffectsAsync(job.ItemId, tenantId, [], UtcNow(), job.Attempt);
        }

        private async Task<List<BatchItemPlan>> PlanAsync(BatchJob job, string tenantId)
        {
            var items = job.Items ?? [];
            var preload = await PreloadAsync(job, items);
            await _jobs.HeartbeatAsync(job.ItemId, tenantId, UtcNow(), job.Attempt);

            var plans = new List<BatchItemPlan>(items.Count);
            foreach (var item in items)
            {
                plans.Add(await PlanItemAsync(job, item, preload));
            }

            return plans;
        }

        /// <summary>One item; an unexpected error fails only that user, never the job.</summary>
        private async Task<BatchItemPlan> PlanItemAsync(BatchJob job, BatchJobItem item, BatchPreload preload)
        {
            string failureType;
            try
            {
                return await _planner.PlanAsync(job, item, preload);
            }
            catch (Exception ex)
            {
                failureType = ex.GetType().Name;
            }

            // Only the exception type is logged, outside the catch: the exception's message could carry the item's
            // email or names (C20).
            _logger.LogError("Batch job {JobId}: item {Index} could not be validated ({ExceptionType})", job.ItemId, item.Index, failureType);
            return new BatchItemPlan
            {
                Item = item,
                Outcome = BatchItemOutcome.Failed,
                Errors = [BatchErrorCodes.Error("data", BatchErrorCodes.WriteFailed)]
            };
        }

        /// <summary>
        /// The bulk reads of §6.5 step 4: users with one query, organizations with one query, roles with one
        /// query per distinct organization, and the submitter's scope.
        /// </summary>
        private async Task<BatchPreload> PreloadAsync(BatchJob job, List<BatchJobItem> items)
        {
            var tenantConfiguration = await _resources.GetTenantConfigurationAsync();
            var multiOrg = tenantConfiguration?.IsMultiOrgEnabled ?? false;
            var memberships = items.SelectMany(i => i.Data.Memberships ?? []).ToList();

            List<string> organizationIds = multiOrg
                ? memberships.Select(m => m.OrganizationId)
                    .Where(id => !string.IsNullOrWhiteSpace(id) && id != IdpConstants.DefaultOrganizationId)
                    .Select(id => id!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
                : [];
            List<Organization> organizations = organizationIds.Count == 0
                ? []
                : await _resources.GetOrganizationsByIdsAsync(organizationIds);
            var organizationsById = organizations
                .Where(o => !string.IsNullOrWhiteSpace(o.ItemId))
                .GroupBy(o => o.ItemId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var liveRoles = await LoadLiveRolesAsync(memberships, multiOrg, organizationsById);

            var users = await _users.GetUsersByEmailsAsync(items.Select(i => i.Data.Email ?? string.Empty).ToList());
            var usersByEmail = users
                .Where(u => !string.IsNullOrWhiteSpace(u.Email))
                .GroupBy(u => UserIdentityRules.NormalizeEmail(u.Email), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            return new BatchPreload(multiOrg, organizationsById, liveRoles, usersByEmail, await BuildCallerAsync(job));
        }

        private async Task<Dictionary<string, IReadOnlySet<string>>> LoadLiveRolesAsync(
            List<BatchMembership> memberships, bool multiOrg, Dictionary<string, Organization> organizations)
        {
            var slugsByOrganization = memberships
                .Where(m => m.Roles is { Count: > 0 })
                .Select(m => (OrganizationId: ResolveOrganizationForRoles(m.OrganizationId, multiOrg, organizations), m.Roles!))
                .Where(x => x.OrganizationId is not null)
                .GroupBy(x => x.OrganizationId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Item2).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

            var liveRoles = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
            foreach (var (organizationId, slugs) in slugsByOrganization)
            {
                var roles = await _resources.GetRolesBySlugAndOrgAsync(slugs, organizationId);
                liveRoles[organizationId] = roles
                    .Where(r => !r.IsArchived && !string.IsNullOrWhiteSpace(r.Slug))
                    .Select(r => r.Slug)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return liveRoles;
        }

        /// <summary>The organization whose roles a membership needs, or null when the membership will fail anyway.</summary>
        private static string? ResolveOrganizationForRoles(string? organizationId, bool multiOrg, Dictionary<string, Organization> organizations)
        {
            var isDefault = string.IsNullOrWhiteSpace(organizationId) || organizationId == IdpConstants.DefaultOrganizationId;
            if (!multiOrg)
            {
                return isDefault ? IdpConstants.DefaultOrganizationId : null;
            }

            if (isDefault)
            {
                return string.IsNullOrWhiteSpace(organizationId) ? null : IdpConstants.DefaultOrganizationId;
            }

            return organizations.ContainsKey(organizationId!) ? organizationId : null;
        }

        /// <summary>
        /// The submitter, re-read now: roles and permissions can change while the job waits. An organization
        /// caller gets its grant scope in its organization. A user or client of the default organization is the
        /// system user for the grant rules, but must still hold manage-user-roles and update-user-profiles
        /// itself (Q1: B), so its own roles and direct permissions in the default organization are read. Only a
        /// platform call with no user and no client needs neither.
        /// </summary>
        private async Task<BatchCaller> BuildCallerAsync(BatchJob job)
        {
            var userId = string.IsNullOrWhiteSpace(job.RequestedBy) ? null : job.RequestedBy;
            var clientId = userId is null && !string.IsNullOrWhiteSpace(job.RequestedByClientId) ? job.RequestedByClientId : string.Empty;

            if (!job.RequestedBySystemCaller)
            {
                // Fails closed: an organization caller without a recorded organization matches no membership
                // (organization_not_allowed) and holds nothing; it is never widened to the default organization.
                var organizationId = job.RequestedByOrganizationId ?? string.Empty;
                var scope = await _accessGrant.GetScopeAsync(new GrantCaller(userId ?? string.Empty, organizationId, false, clientId));
                return new BatchCaller(false, organizationId, scope, scope.OwnPermissions);
            }

            if (userId is null && clientId.Length == 0)
            {
                return new BatchCaller(true, IdpConstants.DefaultOrganizationId, null, null);
            }

            var own = await _accessGrant.GetScopeAsync(new GrantCaller(userId ?? string.Empty, IdpConstants.DefaultOrganizationId, false, clientId));
            return new BatchCaller(true, IdpConstants.DefaultOrganizationId, null, own.OwnPermissions);
        }

        private sealed record WriteResult(BatchJobSummary Summary, List<BatchItemFailure> Failures);

        /// <summary>What the write did, and the side effects still to send for the users it wrote.</summary>
        private sealed record WriteOutcome(
            WriteResult Result,
            List<BatchPendingSideEffect> SideEffects,
            IReadOnlyDictionary<string, User> UsersById);

        /// <summary>§6.5 step 7: persist the side-effect plan, then one bulk write. Step 8 (sending) is the caller's.</summary>
        private async Task<WriteOutcome> WriteAsync(BatchJob job, string tenantId, List<BatchItemPlan> plans)
        {
            var failures = new List<BatchItemFailure>(job.Failures);
            failures.AddRange(plans.Where(p => p.Outcome == BatchItemOutcome.Failed).Select(p => p.ToFailure()));

            var writes = plans.Where(p => p.Outcome is BatchItemOutcome.Created or BatchItemOutcome.Updated).ToList();
            var summary = new BatchJobSummary { Unchanged = plans.Count(p => p.Outcome == BatchItemOutcome.Unchanged) };
            if (writes.Count == 0)
            {
                summary.Failed = failures.Count;
                return new WriteOutcome(new WriteResult(summary, failures), [], new Dictionary<string, User>());
            }

            var sideEffects = writes.SelectMany(p => p.SideEffects).ToList();
            await _jobs.SetPendingSideEffectsAsync(job.ItemId, tenantId, sideEffects, UtcNow(), job.Attempt);

            var inserts = writes.Where(p => p.Outcome == BatchItemOutcome.Created).Select(p => p.User!).ToList();
            var updates = writes.Where(p => p.Outcome == BatchItemOutcome.Updated).Select(p => new UserBulkUpdate(p.User!.ItemId, p.Update!)).ToList();
            var written = await _users.BulkWriteUsersAsync(inserts, updates);

            var usersById = new Dictionary<string, User>(StringComparer.Ordinal);
            foreach (var plan in writes)
            {
                var userId = plan.User!.ItemId;
                var code = WriteErrorCode(written, userId);
                if (code is not null)
                {
                    failures.Add(new BatchItemFailure
                    {
                        Ref = plan.Item.Ref,
                        Index = plan.Item.Index,
                        Email = plan.Item.Data.Email,
                        Errors = [BatchErrorCodes.Error(BatchRequestValidator.EmailField, code)]
                    });
                    sideEffects.RemoveAll(s => s.UserId == userId);
                    continue;
                }

                usersById[userId] = plan.User;
                if (plan.Outcome == BatchItemOutcome.Created) summary.Created++;
                else summary.Updated++;
            }

            if (usersById.Count < writes.Count)
            {
                // A resume must not replay the side effects of users that were never written.
                await _jobs.SetPendingSideEffectsAsync(job.ItemId, tenantId, sideEffects, UtcNow(), job.Attempt);
            }

            summary.Failed = failures.Count;
            _logger.LogInformation(
                "Batch job {JobId} wrote {Created} created, {Updated} updated; {Failed} failed",
                job.ItemId, summary.Created, summary.Updated, summary.Failed);
            return new WriteOutcome(new WriteResult(summary, failures), sideEffects, usersById);
        }

        /// <summary>The failure code of a user the bulk write did not write, or null when it was written.</summary>
        private static string? WriteErrorCode(UserBulkWriteResult written, string userId)
        {
            if (written.DuplicateUserIds.Contains(userId))
            {
                return BatchErrorCodes.EmailConflict;
            }

            return written.FailedUserIds.Contains(userId) ? BatchErrorCodes.WriteFailed : null;
        }

        /// <summary>Per user and best effort: a side effect that fails is logged and never fails the job.</summary>
        private async Task SendSideEffectsAsync(
            BatchJob job, string tenantId, List<BatchPendingSideEffect> sideEffects, IReadOnlyDictionary<string, User> usersById)
        {
            for (var i = 0; i < sideEffects.Count; i++)
            {
                if (i > 0 && i % SideEffectsPerHeartbeat == 0)
                {
                    await _jobs.HeartbeatAsync(job.ItemId, tenantId, UtcNow(), job.Attempt);
                }

                var sideEffect = sideEffects[i];
                if (!usersById.TryGetValue(sideEffect.UserId, out var user))
                {
                    _logger.LogWarning("Batch job {JobId}: user {UserId} of a pending side effect was not found", job.ItemId, sideEffect.UserId);
                    continue;
                }

                try
                {
                    if (sideEffect.Kind == BatchSideEffectKinds.Created)
                    {
                        await _userMutations.PublishBatchUserCreatedAsync(user, sideEffect.OrganizationId, sideEffect.SendEmail, job.RequestedBy);
                    }
                    else if (sideEffect.Kind == BatchSideEffectKinds.MemberAdded && sideEffect.SendEmail)
                    {
                        await _userMutations.NotifyOrganizationMemberAddedAsync(user, sideEffect.OrganizationId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Batch job {JobId}: side effect {Kind} for user {UserId} failed", job.ItemId, sideEffect.Kind, sideEffect.UserId);
                }
            }
        }

        private async Task FinishAsync(BatchJob job, string tenantId, WriteResult result)
        {
            var status = result.Failures.Count == 0 ? BatchJobStatus.Completed : BatchJobStatus.CompletedWithErrors;
            var finish = new BatchJobFinish(status, result.Summary, result.Failures, null, UtcNow());
            if (await _jobs.FinishAsync(job.ItemId, tenantId, finish, RunningStatus, job.Attempt))
            {
                _logger.LogInformation("Batch job {JobId} finished -- {Status}", job.ItemId, status);
                await NotifyFinishedAsync(job, status, result.Summary);
            }
        }

        /// <summary>Fails the whole job. Only the call that moves it to Failed sends the finished notification.</summary>
        private async Task FailAsync(BatchJob job, string tenantId, string error, IReadOnlyCollection<string> fromStatuses, int? attempt = null)
        {
            var summary = new BatchJobSummary { Failed = job.Summary.Failed };
            var finish = new BatchJobFinish(BatchJobStatus.Failed, summary, job.Failures, error, UtcNow());
            if (await _jobs.FinishAsync(job.ItemId, tenantId, finish, fromStatuses, attempt))
            {
                await NotifyFinishedAsync(job, BatchJobStatus.Failed, summary);
            }
        }

        /// <summary>
        /// The finished notification to the submitting user (§6.7). A client has no recipient and reads the job
        /// instead. Best effort: a failure is logged by the sender and never changes the job.
        /// </summary>
        private Task NotifyFinishedAsync(BatchJob job, string status, BatchJobSummary summary)
        {
            if (string.IsNullOrWhiteSpace(job.RequestedBy))
            {
                return Task.CompletedTask;
            }

            return _notificationSender.TrySendAsync(new BuiltInNotification(
                job.TenantId,
                BuiltInNotificationNames.BatchJobFinished,
                BuiltInNotificationSender.Recipients(job.RequestedBy),
                BuiltInNotificationSender.Payload(
                    BuiltInNotificationNames.BatchJobFinished,
                    "Batch import finished",
                    $"{summary.Created} created, {summary.Updated} updated, {summary.Unchanged} unchanged, {summary.Failed} failed.",
                    isSuccess: summary.Failed == 0 && status != BatchJobStatus.Failed,
                    fields: new Dictionary<string, object?>
                    {
                        ["jobId"] = job.ItemId,
                        ["resource"] = job.Resource,
                        ["status"] = status,
                        ["total"] = job.Total,
                        ["created"] = summary.Created,
                        ["updated"] = summary.Updated,
                        ["unchanged"] = summary.Unchanged,
                        ["failed"] = summary.Failed
                    }),
                job.RequestedByOrganizationId));
        }

        #endregion
    }
}
