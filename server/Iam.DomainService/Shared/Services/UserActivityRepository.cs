using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Activity.RequestModel;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Services
{
    public class UserActivityRepository : BaseRepository, IUserActivityRepository
    {
        /// <summary>The <c>Event</c> value of a signup-link redemption.</summary>
        public const string SignupLinkRedeemEvent = "signup-link-redeem";

        // The old redemption collection. Read only: for the live listCollections check and for
        // counting rows that are not copied into UserActivitys yet. Nothing writes it any more.
        private const string LegacyRedemptionCollection = "SignupLinkRedemptions";

        private readonly ILogger<UserActivityRepository> _logger;

        public UserActivityRepository(IDbContextProvider dbContextProvider, ILogger<UserActivityRepository> logger, IIamDataAccess dataAccess)
            : base(dbContextProvider, dataAccess)
        {
            _logger = logger;
        }

        public async Task InsertAsync(UserActivity activity, CancellationToken ct)
        {
            var collection = GetCollection<UserActivity>();
            await _dataAccess.EnsureInsertAllowedAsync(IamSchemas.UserActivity, "UserActivityRepository.InsertAsync");
            await _dataAccess.PrepareWriteAsync(IamSchemas.UserActivity, activity, isInsert: true, "UserActivityRepository.InsertAsync");
            try
            {
                await collection.InsertOneAsync(activity, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogDebug("Duplicate UserActivity MessageId={MessageId} skipped (idempotent).", activity.MessageId);
            }
        }

        public async Task<List<UserActivity>> GetAsync(string userId, GetActivitiesRequest req, CancellationToken ct)
        {
            var collection = GetCollection<UserActivity>();
            var (_, filter, sort) = Build(userId, req);
            var skip = req.PageSize * req.Page;
            var options = new FindOptions<UserActivity>
            {
                Sort = sort,
                Skip = skip,
                Limit = req.PageSize
            };
            var scope = await _dataAccess.ReadAsync(IamSchemas.UserActivity, filter, "UserActivityRepository.GetAsync", useProjection: false);
            if (!scope.Granted) return [];
            var cursor = await collection.FindAsync(scope.Filter, options, ct);
            return scope.ForView(await cursor.ToListAsync(ct));
        }

        public async Task<long> CountAsync(string userId, GetActivitiesRequest req, CancellationToken ct)
        {
            var collection = GetCollection<UserActivity>();
            var (_, filter, _) = Build(userId, req);
            var scope = await _dataAccess.ReadAsync(IamSchemas.UserActivity, filter, "UserActivityRepository.CountAsync", useProjection: false);
            if (!scope.Granted) return 0;
            return await collection.CountDocumentsAsync(scope.Filter, cancellationToken: ct);
        }

        public async Task<LoginHistory?> GetLoginHistoryAsync(string userId, string loginEvent, string? ipAddress, string? userAgent, CancellationToken ct)
        {
            var b = Builders<UserActivity>.Filter;
            var earlier = b.Eq(x => x.UserId, userId)
                & b.Eq(x => x.Event, loginEvent)
                & b.Eq(x => x.Outcome, "success");

            var anyScope = await _dataAccess.ReadAsync(IamSchemas.UserActivity, earlier, "UserActivityRepository.GetLoginHistoryAsync", useProjection: false);
            if (!anyScope.Granted) return null;

            var collection = GetCollection<UserActivity>();
            var hasEarlier = await collection.CountDocumentsAsync(anyScope.Filter, new CountOptions { Limit = 1 }, ct) > 0;
            if (!hasEarlier)
            {
                return new LoginHistory(false, false);
            }

            var sameDevice = earlier
                & b.Eq(x => x.Context!.IpAddress, ipAddress)
                & b.Eq(x => x.Context!.UserAgent, userAgent);
            var deviceScope = await _dataAccess.ReadAsync(IamSchemas.UserActivity, sameDevice, "UserActivityRepository.GetLoginHistoryAsync", useProjection: false);
            if (!deviceScope.Granted) return null;

            var seen = await collection.CountDocumentsAsync(deviceScope.Filter, new CountOptions { Limit = 1 }, ct) > 0;
            return new LoginHistory(true, seen);
        }

        public async Task<long> CountSignupLinkRejectionsAsync(
            string tenantId,
            DateTime fromUtc,
            DateTime toUtc,
            IEnumerable<string> linkIds,
            CancellationToken ct = default)
        {
            var ids = linkIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList()
                ?? new List<string>();
            if (ids.Count == 0)
            {
                return 0;
            }

            var legacyExists = await LegacyRedemptionCollectionExistsAsync(ct);

            var b = Builders<UserActivity>.Filter;
            var events = b.Eq(x => x.TenantId, tenantId)
                & b.Eq(x => x.Event, SignupLinkRedeemEvent)
                & b.Eq(x => x.Outcome, nameof(Iam.DomainService.SignupLinks.SignupLinkRedemptionOutcome.Rejected))
                & b.Gte(x => x.CreatedDate, fromUtc)
                & b.Lt(x => x.CreatedDate, toUtc)
                & b.In(x => x.EntityId, ids);

            if (legacyExists)
            {
                // An event copied from an old row by the migration: that row is counted below. After cleanup
                // the old collection is gone, so these events are the only record and are counted.
                events &= b.Exists("MigratedFrom", false);
            }

            var eventScope = await _dataAccess.ReadAsync(IamSchemas.UserActivity, events, "UserActivityRepository.CountSignupLinkRejectionsAsync", useProjection: false);
            var count = eventScope.Granted
                ? await GetCollection<UserActivity>().CountDocumentsAsync(eventScope.Filter, cancellationToken: ct)
                : 0;

            if (legacyExists)
            {
                var o = Builders<BsonDocument>.Filter;
                var rows = o.Eq("TenantId", tenantId)
                    & o.Eq("Outcome", (int)Iam.DomainService.SignupLinks.SignupLinkRedemptionOutcome.Rejected)
                    & o.Gte("RedeemedAtUtc", fromUtc)
                    & o.Lt("RedeemedAtUtc", toUtc)
                    & o.In("LinkId", ids)
                    // Written back from an event by a rollback: the event is counted above.
                    & o.Exists("ReversedFromActivityId", false);

                count += await GetCollectionByName<BsonDocument>(LegacyRedemptionCollection)
                    .CountDocumentsAsync(rows, cancellationToken: ct);
            }

            return count;
        }

        private async Task<bool> LegacyRedemptionCollectionExistsAsync(CancellationToken ct)
        {
            var database = _dbContextProvider.GetDatabase();
            if (database is null)
            {
                return false;
            }

            var options = new ListCollectionNamesOptions
            {
                Filter = new BsonDocument("name", LegacyRedemptionCollection)
            };
            var cursor = await database.ListCollectionNamesAsync(options, ct);
            return (await cursor.ToListAsync(ct)).Count > 0;
        }

        private static (string userId, FilterDefinition<UserActivity> filter, SortDefinition<UserActivity> sort) Build(string userId, GetActivitiesRequest req)
        {
            var b = Builders<UserActivity>.Filter;
            var f = req.Filter;
            var context = BlocksContext.GetContext();

            var filters = new List<FilterDefinition<UserActivity>>();

            var orgId = !string.IsNullOrWhiteSpace(f?.OrganizationId)
                ? f!.OrganizationId
                : context?.OrganizationId;
            if (!string.IsNullOrWhiteSpace(orgId))
            {
                filters.Add(b.Eq(x => x.OrganizationId, orgId));
            }

            var tenantId = !string.IsNullOrWhiteSpace(f?.TenantId)
                ? f!.TenantId
                : context?.TenantId;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                filters.Add(b.Eq(x => x.TenantId, tenantId));
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                filters.Add(b.Eq(x => x.UserId, userId));
            }

            if (f is null)
            {
                return (userId,
                        filters.Count > 0 ? b.And(filters) : b.Empty,
                        Builders<UserActivity>.Sort.Descending(x => x.CreatedDate));
            }

            if (!string.IsNullOrWhiteSpace(f.ActorUserId))
            {
                filters.Add(b.Eq(x => x.ActorUserId, f.ActorUserId));
            }

            if (!string.IsNullOrWhiteSpace(f.ActorUserId))
            {
                filters.Add(b.Eq(x => x.ActorUserId, f.ActorUserId));
            }

            if (f.Categories is { Count: > 0 })
            {
                filters.Add(b.In(x => x.Category, f.Categories));
            }

            if (f.Events is { Count: > 0 })
            {
                filters.Add(b.In(x => x.Event, f.Events));
            }

            if (f.Outcomes is { Count: > 0 })
            {
                filters.Add(b.In(x => x.Outcome, f.Outcomes));
            }

            if (f.Severities is { Count: > 0 })
            {
                filters.Add(b.In(x => x.Severity, f.Severities));
            }

            if (!string.IsNullOrWhiteSpace(f.Source))
            {
                filters.Add(b.Eq(x => x.Source, f.Source));
            }

            if (!string.IsNullOrWhiteSpace(f.SessionId))
            {
                filters.Add(b.Eq(x => x.SessionId, f.SessionId));
            }

            if (!string.IsNullOrWhiteSpace(f.ClientId))
            {
                filters.Add(b.Eq(x => x.ClientId, f.ClientId));
            }

            if (!string.IsNullOrWhiteSpace(f.CorrelationId))
            {
                filters.Add(b.Eq(x => x.CorrelationId, f.CorrelationId));
            }

            if (!string.IsNullOrWhiteSpace(f.Entity))
            {
                filters.Add(b.Eq(x => x.Entity, f.Entity));
            }

            if (!string.IsNullOrWhiteSpace(f.EntityId))
            {
                filters.Add(b.Eq(x => x.EntityId, f.EntityId));
            }

            if (f.From.HasValue)
            {
                filters.Add(b.Gte(x => x.CreatedDate, f.From.Value));
            }

            if (f.To.HasValue)
            {
                filters.Add(b.Lte(x => x.CreatedDate, f.To.Value));
            }

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var regex = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(f.Search), "i");
                filters.Add(b.Or(
                    b.Regex(x => x.Event, regex),
                    b.Regex("Metadata.Values", regex)
                ));
            }

            var combined = filters.Count > 0 ? b.And(filters) : b.Empty;

            var sortDirection = req.Sort?.IsDescending == false
                ? SortDirection.Ascending
                : SortDirection.Descending;
            var sortDef = req.Sort?.Property switch
            {
                "CreatedDate" or null => sortDirection == SortDirection.Descending
                    ? Builders<UserActivity>.Sort.Descending(x => x.CreatedDate)
                    : Builders<UserActivity>.Sort.Ascending(x => x.CreatedDate),
                _ => sortDirection == SortDirection.Descending
                    ? Builders<UserActivity>.Sort.Descending(req.Sort.Property)
                    : Builders<UserActivity>.Sort.Ascending(req.Sort.Property)
            };

            return (userId, combined, sortDef);
        }
    }
}