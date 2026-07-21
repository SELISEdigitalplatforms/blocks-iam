using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Activity.RequestModel;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Iam.DomainService.Services
{
    public class UserActivityRepository : BaseRepository, IUserActivityRepository
    {
        private readonly ILogger<UserActivityRepository> _logger;

        public UserActivityRepository(IDbContextProvider dbContextProvider, ILogger<UserActivityRepository> logger)
            : base(dbContextProvider)
        {
            _logger = logger;
        }

        public async Task InsertAsync(UserActivity activity, CancellationToken ct)
        {
            var collection = GetCollection<UserActivity>();
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
            var cursor = await collection.FindAsync(filter, options, ct);
            return await cursor.ToListAsync(ct);
        }

        public async Task<long> CountAsync(string userId, GetActivitiesRequest req, CancellationToken ct)
        {
            var collection = GetCollection<UserActivity>();
            var (_, filter, _) = Build(userId, req);
            return await collection.CountDocumentsAsync(filter, cancellationToken: ct);
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