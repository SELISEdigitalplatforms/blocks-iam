using MongoDB.Driver;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Repositories
{
    public sealed class DeviceAuthorizationRepository : IDeviceAuthorizationRepository
    {
        private const string CollectionName = "DeviceAuthorizationRequests";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<DeviceAuthorizationRepository> _logger;

        public DeviceAuthorizationRepository(
            IDbContextProvider dbContextProvider,
            ILogger<DeviceAuthorizationRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        private IMongoCollection<DeviceAuthorizationRequestModel> Collection() =>
            GetDatabase().GetCollection<DeviceAuthorizationRequestModel>(CollectionName);

        private IMongoCollection<DeviceAuthorizationRequestModel> Collection(string tenantId) =>
            _dbContextProvider.GetDatabase(tenantId).GetCollection<DeviceAuthorizationRequestModel>(CollectionName);

        public async Task EnsureIndexesAsync(CancellationToken ct = default)
        {
            try
            {
                await EnsureIndexesAsync(Collection(), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeviceAuthorizationRepository.EnsureIndexesAsync failed; index creation is idempotent and will be retried.");
            }
        }

        // Background work has no request context. Let the worker observe failures so it
        // can retry index creation for this tenant on its next sweep.
        public Task EnsureIndexesAsync(string tenantId, CancellationToken ct = default) =>
            EnsureIndexesAsync(Collection(tenantId), ct);

        private static async Task EnsureIndexesAsync(IMongoCollection<DeviceAuthorizationRequestModel> collection, CancellationToken ct)
        {
            var keys = Builders<DeviceAuthorizationRequestModel>.IndexKeys;
            await collection.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<DeviceAuthorizationRequestModel>(
                    keys.Ascending(x => x.DeviceCodeHash),
                    new CreateIndexOptions<DeviceAuthorizationRequestModel> { Name = "ix_device_code_hash_unique", Unique = true }),
                // Terminal rows remain as an audit trail, so uniqueness covers every status.
                new CreateIndexModel<DeviceAuthorizationRequestModel>(
                    keys.Ascending(x => x.UserCode),
                    new CreateIndexOptions<DeviceAuthorizationRequestModel> { Name = "ix_user_code_unique", Unique = true }),
                new CreateIndexModel<DeviceAuthorizationRequestModel>(
                    keys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions<DeviceAuthorizationRequestModel> { Name = "ix_expires_at" })
            }, ct);
        }

        public async Task CreateAsync(DeviceAuthorizationRequestModel entity, CancellationToken ct = default)
        {
            await Collection().InsertOneAsync(entity, cancellationToken: ct);
            _logger.LogInformation("Device authorization request persisted {Id} for client {ClientId}", entity.Id, entity.ClientId);
        }

        public async Task<DeviceAuthorizationRequestModel?> GetByDeviceCodeHashAsync(string hash, CancellationToken ct = default)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.DeviceCodeHash, hash);
            return await Collection().Find(filter).FirstOrDefaultAsync(ct);
        }

        public async Task<DeviceAuthorizationRequestModel?> GetByUserCodeAsync(string userCode, CancellationToken ct = default)
        {
            // The unique index makes a live duplicate impossible going forward, but this stays
            // defensive against any pre-existing duplicate rows: newest-first so the row actually
            // relevant to the caller — not whatever order the storage engine happens to return —
            // is the one picked.
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.UserCode, userCode);
            var sort = Builders<DeviceAuthorizationRequestModel>.Sort.Descending(x => x.CreatedAt);
            return await Collection().Find(filter).Sort(sort).FirstOrDefaultAsync(ct);
        }

        public async Task<DeviceAuthorizationRequestModel?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id);
            return await Collection().Find(filter).FirstOrDefaultAsync(ct);
        }

        public async Task<bool> MarkApprovedAsync(string id, string userId, DateTime at, CancellationToken ct = default, string? organizationId = null)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Status, DeviceAuthorizationStatus.Pending));

            var update = Builders<DeviceAuthorizationRequestModel>.Update
                .Set(x => x.Status, DeviceAuthorizationStatus.Approved)
                .Set(x => x.UserId, userId)
                .Set(x => x.ApprovedAt, at)
                .Set(x => x.OrganizationId, organizationId);

            var result = await Collection().UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> MarkDeniedAsync(string id, DateTime at, CancellationToken ct = default)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Status, DeviceAuthorizationStatus.Pending));

            var update = Builders<DeviceAuthorizationRequestModel>.Update
                .Set(x => x.Status, DeviceAuthorizationStatus.Denied)
                .Set(x => x.DeniedAt, at);

            var result = await Collection().UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> MarkConsumedAsync(string id, DateTime at, CancellationToken ct = default)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Status, DeviceAuthorizationStatus.Approved),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.ConsumedAt, null));

            var update = Builders<DeviceAuthorizationRequestModel>.Update
                .Set(x => x.Status, DeviceAuthorizationStatus.Consumed)
                .Set(x => x.ConsumedAt, at);

            var result = await Collection().UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }

        public Task<bool> MarkExpiredAsync(IEnumerable<string> ids, CancellationToken ct = default) =>
            MarkExpiredAsync(Collection(), ids, ct);

        public Task<bool> MarkExpiredAsync(string tenantId, IEnumerable<string> ids, CancellationToken ct = default) =>
            MarkExpiredAsync(Collection(tenantId), ids, ct);

        private static async Task<bool> MarkExpiredAsync(
            IMongoCollection<DeviceAuthorizationRequestModel> collection, IEnumerable<string> ids, CancellationToken ct)
        {
            var idList = ids?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList() ?? new List<string>();
            if (idList.Count == 0)
            {
                return false;
            }

            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.In(x => x.Id, idList),
                Builders<DeviceAuthorizationRequestModel>.Filter.In(x => x.Status,
                    new[] { DeviceAuthorizationStatus.Pending, DeviceAuthorizationStatus.Approved }));

            var update = Builders<DeviceAuthorizationRequestModel>.Update.Set(x => x.Status, DeviceAuthorizationStatus.Expired);

            var result = await collection.UpdateManyAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> SetApprovalTokenHashAsync(string id, string approvalTokenHash, CancellationToken ct = default)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Status, DeviceAuthorizationStatus.Pending));

            var update = Builders<DeviceAuthorizationRequestModel>.Update.Set(x => x.ApprovalTokenHash, approvalTokenHash);
            var result = await Collection().UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> UpdatePollAsync(string id, DateTime lastPollAt, int pollsObserved, CancellationToken ct = default)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id);
            var update = Builders<DeviceAuthorizationRequestModel>.Update
                .Set(x => x.LastPollAt, lastPollAt)
                .Set(x => x.PollsObserved, pollsObserved);

            var result = await Collection().UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> TryRecordPollAsync(string id, DateTime previousLastPollAt, DateTime newLastPollAt, int pollsObserved, CancellationToken ct = default)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Status, DeviceAuthorizationStatus.Pending),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.LastPollAt, previousLastPollAt));

            var update = Builders<DeviceAuthorizationRequestModel>.Update
                .Set(x => x.LastPollAt, newLastPollAt)
                .Set(x => x.PollsObserved, pollsObserved);

            var result = await Collection().UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }

        public async Task<int> BumpPollIntervalAsync(string id, int currentInterval, CancellationToken ct = default)
            => await BumpPollIntervalAsync(id, currentInterval, 5, ct);

        public async Task<int> BumpPollIntervalAsync(string id, int currentInterval, int incrementSeconds, CancellationToken ct = default)
        {
            var newInterval = currentInterval + Math.Max(1, incrementSeconds);
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Id, id),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.Status, DeviceAuthorizationStatus.Pending),
                Builders<DeviceAuthorizationRequestModel>.Filter.Eq(x => x.PollIntervalSeconds, currentInterval));
            var update = Builders<DeviceAuthorizationRequestModel>.Update.Set(x => x.PollIntervalSeconds, newInterval);
            var result = await Collection().UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0 ? newInterval : currentInterval;
        }

        public Task<IReadOnlyList<string>> GetExpiredIdsAsync(DateTime olderThanUtc, int limit, CancellationToken ct = default) =>
            GetExpiredIdsAsync(Collection(), olderThanUtc, limit, ct);

        public Task<IReadOnlyList<string>> GetExpiredIdsAsync(
            string tenantId, DateTime olderThanUtc, int limit, CancellationToken ct = default) =>
            GetExpiredIdsAsync(Collection(tenantId), olderThanUtc, limit, ct);

        private static async Task<IReadOnlyList<string>> GetExpiredIdsAsync(
            IMongoCollection<DeviceAuthorizationRequestModel> collection, DateTime olderThanUtc, int limit, CancellationToken ct)
        {
            var filter = Builders<DeviceAuthorizationRequestModel>.Filter.And(
                Builders<DeviceAuthorizationRequestModel>.Filter.Lt(x => x.ExpiresAt, olderThanUtc),
                Builders<DeviceAuthorizationRequestModel>.Filter.In(x => x.Status,
                    new[] { DeviceAuthorizationStatus.Pending, DeviceAuthorizationStatus.Approved }));

            var projection = Builders<DeviceAuthorizationRequestModel>.Projection.Include(x => x.Id);

            var docs = await collection
                .Find(filter)
                .Limit(limit)
                .Project<DeviceAuthorizationRequestModel>(projection)
                .ToListAsync(ct);

            return docs.Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
    }
}
