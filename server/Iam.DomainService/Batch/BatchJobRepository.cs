using System.Collections.Concurrent;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Iam.DomainService.Batch
{
    /// <summary>The result of trying to start a job.</summary>
    /// <param name="Job">The job, now <c>Running</c>; null when it was not started.</param>
    /// <param name="Conflict">True when another job of the resource is running (the unique index refused).</param>
    public sealed record BatchJobStart(BatchJob? Job, bool Conflict);

    /// <summary>The final state written in one update when a job finishes.</summary>
    public sealed record BatchJobFinish(
        string Status,
        BatchJobSummary Summary,
        List<BatchItemFailure> Failures,
        string? Error,
        DateTime CompletedAt);

    public interface IBatchJobRepository
    {
        Task InsertAsync(BatchJob job);
        Task<BatchJob?> GetAsync(string jobId, string tenantId);

        /// <summary>
        /// Atomically sets the job <c>Running</c> when it is <c>Queued</c>, or <c>Running</c> with a heartbeat
        /// older than <paramref name="staleBefore"/> (a worker that died). Counts the attempt and sets the heartbeat.
        /// </summary>
        Task<BatchJobStart> TryStartAsync(string jobId, string tenantId, DateTime now, DateTime staleBefore);

        Task<BatchJob?> GetRunningAsync(string resource, string tenantId);

        /// <summary>Puts a running job back to <c>Queued</c> when its heartbeat is still <paramref name="heartbeatAt"/>.</summary>
        Task<bool> RequeueStaleAsync(string jobId, string tenantId, DateTime? heartbeatAt);

        /// <summary>
        /// Updates the heartbeat. With <paramref name="attempt"/>, only while the job is still that attempt: a worker
        /// whose job was taken over never writes over its successor.
        /// </summary>
        Task HeartbeatAsync(string jobId, string tenantId, DateTime now, int? attempt = null);

        /// <summary>Replaces the planned side effects; guarded by <paramref name="attempt"/> like <see cref="HeartbeatAsync"/>.</summary>
        Task SetPendingSideEffectsAsync(string jobId, string tenantId, List<BatchPendingSideEffect> sideEffects, DateTime now, int? attempt = null);

        /// <summary>
        /// Writes the final state and removes the items in one update, only when the job is in one of
        /// <paramref name="fromStatuses"/> (and, when given, still <paramref name="attempt"/>). True for the one call that moved it.
        /// </summary>
        Task<bool> FinishAsync(string jobId, string tenantId, BatchJobFinish finish, IReadOnlyCollection<string> fromStatuses, int? attempt = null);

        Task<BatchJob?> GetOldestQueuedAsync(string resource, string tenantId);
    }

    /// <summary>
    /// <c>BatchJobs</c> in the tenant database, through <see cref="IIamDataAccess"/>. The unique partial
    /// index on running jobs is what lets only one job of a resource run per tenant.
    /// </summary>
    public class BatchJobRepository : IBatchJobRepository
    {
        public const string RunningIndex = "ux_resource_running";
        private const string QueuedIndex = "ix_resource_status_created";
        private const string CreatedIndex = "ix_created_desc";

        private readonly IIdentityAccessManagementRepository _iamRepository;
        private readonly IIamDataAccess _dataAccess;
        private readonly ILogger<BatchJobRepository> _logger;
        private readonly ConcurrentDictionary<string, bool> _indexedTenants = new(StringComparer.Ordinal);

        public BatchJobRepository(
            IIdentityAccessManagementRepository iamRepository,
            IIamDataAccess dataAccess,
            ILogger<BatchJobRepository> logger)
        {
            _iamRepository = iamRepository;
            _dataAccess = dataAccess;
            _logger = logger;
        }

        private IMongoCollection<BatchJob> Collection(string tenantId) => _iamRepository.GetCollection<BatchJob>(tenantId);

        private static FilterDefinitionBuilder<BatchJob> Filter => Builders<BatchJob>.Filter;

        /// <summary>
        /// By id only: the tenant database already isolates tenants, and a job whose own TenantId does not
        /// match must still be found so it can be failed (tenant_mismatch). Readers compare TenantId themselves.
        /// </summary>
        private static FilterDefinition<BatchJob> ById(string jobId) => Filter.Eq(x => x.ItemId, jobId);

        private static FilterDefinition<BatchJob> ByAttempt(string jobId, int? attempt) =>
            attempt is null ? ById(jobId) : ById(jobId) & Filter.Eq(x => x.Attempt, attempt.Value);

        /// <summary>Once per tenant and process; a failure is logged, returns false and is retried on the next call.</summary>
        private async Task<bool> EnsureIndexesAsync(string tenantId)
        {
            if (_indexedTenants.ContainsKey(tenantId))
            {
                return true;
            }

            try
            {
                var keys = Builders<BatchJob>.IndexKeys;
                await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    new CreateIndexModel<BatchJob>(
                        keys.Ascending(x => x.Resource).Ascending(x => x.Status),
                        new CreateIndexOptions<BatchJob>
                        {
                            Name = RunningIndex,
                            Unique = true,
                            PartialFilterExpression = Filter.Eq(x => x.Status, BatchJobStatus.Running)
                        }),
                    new CreateIndexModel<BatchJob>(
                        keys.Ascending(x => x.Resource).Ascending(x => x.Status).Ascending(x => x.CreatedDate),
                        new CreateIndexOptions { Name = QueuedIndex }),
                    new CreateIndexModel<BatchJob>(keys.Descending(x => x.CreatedDate), new CreateIndexOptions { Name = CreatedIndex })
                ]);
                _indexedTenants[tenantId] = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BatchJobRepository.EnsureIndexesAsync failed for tenant {TenantId}; index creation is idempotent and will be retried.", tenantId);
                return false;
            }
        }

        public Task InsertAsync(BatchJob job)
        {
            ArgumentNullException.ThrowIfNull(job);
            return InsertCoreAsync(job);
        }

        private async Task InsertCoreAsync(BatchJob job)
        {
            await EnsureIndexesAsync(job.TenantId);
            await _dataAccess.InsertOneAsync(Collection(job.TenantId), IamSchemas.BatchJob, job, "BatchJobRepository.InsertAsync", job.TenantId);
        }

        public Task<BatchJob?> GetAsync(string jobId, string tenantId) =>
            _dataAccess.FindFirstAsync(Collection(tenantId), IamSchemas.BatchJob, ById(jobId), "BatchJobRepository.GetAsync", tenantId: tenantId);

        public async Task<BatchJobStart> TryStartAsync(string jobId, string tenantId, DateTime now, DateTime staleBefore)
        {
            // Without the unique partial index two jobs of a tenant could run at once, so no job starts until it exists.
            if (!await EnsureIndexesAsync(tenantId))
            {
                throw new InvalidOperationException($"The batch job indexes of tenant {tenantId} could not be ensured; the job is not started.");
            }
            var startable = Filter.Eq(x => x.Status, BatchJobStatus.Queued)
                | (Filter.Eq(x => x.Status, BatchJobStatus.Running) & Filter.Lt(x => x.HeartbeatAt, staleBefore));
            var update = Builders<BatchJob>.Update
                .Set(x => x.Status, BatchJobStatus.Running)
                .Set(x => x.HeartbeatAt, now)
                .Set(x => x.LastUpdatedDate, now)
                .Inc(x => x.Attempt, 1);

            BatchJob? job;
            try
            {
                job = await _dataAccess.FindOneAndUpdateAsync(
                    Collection(tenantId), IamSchemas.BatchJob, ById(jobId) & startable, update,
                    "BatchJobRepository.TryStartAsync",
                    new FindOneAndUpdateOptions<BatchJob> { ReturnDocument = ReturnDocument.After });
            }
            catch (MongoException ex) when (IsDuplicateKey(ex))
            {
                return new BatchJobStart(null, Conflict: true);
            }

            if (job is { StartedAt: null })
            {
                job.StartedAt = now;
                await _dataAccess.UpdateOneAsync(
                    Collection(tenantId), IamSchemas.BatchJob, ById(jobId) & Filter.Eq(x => x.StartedAt, null),
                    Builders<BatchJob>.Update.Set(x => x.StartedAt, now), "BatchJobRepository.TryStartAsync");
            }

            return new BatchJobStart(job, Conflict: false);
        }

        public Task<BatchJob?> GetRunningAsync(string resource, string tenantId) =>
            _dataAccess.FindFirstAsync(
                Collection(tenantId), IamSchemas.BatchJob,
                Filter.Eq(x => x.Resource, resource) & Filter.Eq(x => x.Status, BatchJobStatus.Running) & Filter.Eq(x => x.TenantId, tenantId),
                "BatchJobRepository.GetRunningAsync", tenantId: tenantId);

        public async Task<bool> RequeueStaleAsync(string jobId, string tenantId, DateTime? heartbeatAt)
        {
            var result = await _dataAccess.UpdateOneAsync(
                Collection(tenantId), IamSchemas.BatchJob,
                ById(jobId) & Filter.Eq(x => x.Status, BatchJobStatus.Running) & Filter.Eq(x => x.HeartbeatAt, heartbeatAt),
                Builders<BatchJob>.Update.Set(x => x.Status, BatchJobStatus.Queued).Set(x => x.LastUpdatedDate, DateTime.UtcNow),
                "BatchJobRepository.RequeueStaleAsync");
            return result is { ModifiedCount: 1 };
        }

        public Task HeartbeatAsync(string jobId, string tenantId, DateTime now, int? attempt = null) =>
            _dataAccess.UpdateOneAsync(
                Collection(tenantId), IamSchemas.BatchJob, ByAttempt(jobId, attempt),
                Builders<BatchJob>.Update.Set(x => x.HeartbeatAt, now), "BatchJobRepository.HeartbeatAsync");

        public Task SetPendingSideEffectsAsync(string jobId, string tenantId, List<BatchPendingSideEffect> sideEffects, DateTime now, int? attempt = null) =>
            _dataAccess.UpdateOneAsync(
                Collection(tenantId), IamSchemas.BatchJob, ByAttempt(jobId, attempt),
                Builders<BatchJob>.Update.Set(x => x.PendingSideEffects, sideEffects).Set(x => x.HeartbeatAt, now),
                "BatchJobRepository.SetPendingSideEffectsAsync");

        public Task<bool> FinishAsync(string jobId, string tenantId, BatchJobFinish finish, IReadOnlyCollection<string> fromStatuses, int? attempt = null)
        {
            ArgumentNullException.ThrowIfNull(finish);
            return FinishCoreAsync(jobId, tenantId, finish, fromStatuses, attempt);
        }

        private async Task<bool> FinishCoreAsync(string jobId, string tenantId, BatchJobFinish finish, IReadOnlyCollection<string> fromStatuses, int? attempt)
        {
            var update = Builders<BatchJob>.Update
                .Set(x => x.Status, finish.Status)
                .Set(x => x.Summary, finish.Summary)
                .Set(x => x.Failures, finish.Failures.OrderBy(f => f.Index).ToList())
                .Set(x => x.Error, finish.Error)
                .Set(x => x.CompletedAt, finish.CompletedAt)
                .Set(x => x.LastUpdatedDate, finish.CompletedAt)
                .Set(x => x.PendingSideEffects, new List<BatchPendingSideEffect>())
                .Unset(x => x.Items);
            var result = await _dataAccess.UpdateOneAsync(
                Collection(tenantId), IamSchemas.BatchJob, ByAttempt(jobId, attempt) & Filter.In(x => x.Status, fromStatuses),
                update, "BatchJobRepository.FinishAsync");
            return result is { ModifiedCount: 1 };
        }

        public async Task<BatchJob?> GetOldestQueuedAsync(string resource, string tenantId)
        {
            var filter = Filter.Eq(x => x.Resource, resource) & Filter.Eq(x => x.Status, BatchJobStatus.Queued) & Filter.Eq(x => x.TenantId, tenantId);
            var scope = await _dataAccess.ReadAsync(IamSchemas.BatchJob, filter, "BatchJobRepository.GetOldestQueuedAsync", tenantId, useProjection: false);
            if (!scope.Granted)
            {
                return null;
            }

            return await Collection(tenantId).Find(scope.Filter).SortBy(x => x.CreatedDate).Limit(1).FirstOrDefaultAsync();
        }

        private static bool IsDuplicateKey(MongoException ex) =>
            ex is MongoCommandException { Code: 11000 }
            || ex is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey };
    }
}
