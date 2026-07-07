using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using MongoDB.Driver;

namespace Authentication.DomainService.Oidc.Repositories
{
    public sealed class AuditLogRepository : IAuditLogRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public AuditLogRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public IMongoCollection<T> GetCollection<T>()
        {
            return _dbContextProvider.GetCollection<T>($"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollection<T>(string tenantId)
        {
            return _dbContextProvider.GetCollection<T>(tenantId, $"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollectionByName<T>(string collectionName, string tenantId)
        {
            return _dbContextProvider.GetCollection<T>(tenantId, collectionName);
        }

        public IMongoCollection<T> GetCollectionByName<T>(string collectionName)
        {
            return _dbContextProvider.GetCollection<T>(collectionName);
        }

        public async Task<string> CreateAsync(AuditLogModel log)
        {
            var collection = GetCollectionByName<AuditLogModel>("IdpAuditLogs");
            await collection.InsertOneAsync(log);
            return log.Id;
        }

        public async Task<IEnumerable<AuditLogModel>> GetByUserAsync(string userId, string tenantId, DateTime from, DateTime to)
        {
            var collection = GetCollection<AuditLogModel>("IdpAuditLogs");
            var filter = Builders<AuditLogModel>.Filter.And(
                Builders<AuditLogModel>.Filter.Eq(l => l.UserId, userId),
                Builders<AuditLogModel>.Filter.Eq(l => l.TenantId, tenantId),
                Builders<AuditLogModel>.Filter.Gte(l => l.Timestamp, from),
                Builders<AuditLogModel>.Filter.Lte(l => l.Timestamp, to)
            );
            return await collection.Find(filter).SortByDescending(l => l.Timestamp).ToListAsync();
        }

        public async Task<IEnumerable<AuditLogModel>> GetByEventTypeAsync(string eventType, DateTime from, DateTime to)
        {
            var collection = GetCollection<AuditLogModel>("IdpAuditLogs");
            var filter = Builders<AuditLogModel>.Filter.And(
                Builders<AuditLogModel>.Filter.Eq(l => l.EventType, eventType),
                Builders<AuditLogModel>.Filter.Gte(l => l.Timestamp, from),
                Builders<AuditLogModel>.Filter.Lte(l => l.Timestamp, to)
            );
            return await collection.Find(filter).SortByDescending(l => l.Timestamp).ToListAsync();
        }

        public async Task<IEnumerable<AuditLogModel>> GetBySeverityAsync(string severity, DateTime from, DateTime to)
        {
            var collection = GetCollection<AuditLogModel>("IdpAuditLogs");
            var filter = Builders<AuditLogModel>.Filter.And(
                Builders<AuditLogModel>.Filter.Eq(l => l.Severity, severity),
                Builders<AuditLogModel>.Filter.Gte(l => l.Timestamp, from),
                Builders<AuditLogModel>.Filter.Lte(l => l.Timestamp, to)
            );
            return await collection.Find(filter).SortByDescending(l => l.Timestamp).ToListAsync();
        }

        public async Task<long> GetCountAsync(string eventType = null, DateTime? from = null, DateTime? to = null)
        {
            var collection = GetCollectionByName<AuditLogModel>("IdpAuditLogs");
            var filterBuilder = Builders<AuditLogModel>.Filter;
            var filters = new List<FilterDefinition<AuditLogModel>>();

            if (!string.IsNullOrEmpty(eventType))
                filters.Add(filterBuilder.Eq(l => l.EventType, eventType));

            if (from.HasValue)
                filters.Add(filterBuilder.Gte(l => l.Timestamp, from.Value));

            if (to.HasValue)
                filters.Add(filterBuilder.Lte(l => l.Timestamp, to.Value));

            var filter = filters.Count > 0 ? filterBuilder.And(filters) : filterBuilder.Empty;
            return await collection.CountDocumentsAsync(filter);
        }
    }
}