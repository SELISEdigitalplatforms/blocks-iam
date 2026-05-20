using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Authentication.DomainService.Oidc.Repositories
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<AuditLogRepository> _logger;

        public AuditLogRepository(
            IDbContextProvider dbContextProvider,
            ILogger<AuditLogRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
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

        public async Task<string> CreateAsync(AuditLogModel log, string? tenant = null)
        {
            try
            {
                var collection = tenant == null ? GetCollectionByName<AuditLogModel>("IdpAuditLogs") : GetCollectionByName<AuditLogModel>("IdpAuditLogs", tenant);
                await collection.InsertOneAsync(log);
                return log.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating audit log: {log.EventType}");
                throw;
            }
        }

        public async Task<IEnumerable<AuditLogModel>> GetByUserAsync(string userId, string tenantId, DateTime from, DateTime to)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching audit logs for user: {userId}");
                throw;
            }
        }

        public async Task<IEnumerable<AuditLogModel>> GetByEventTypeAsync(string eventType, DateTime from, DateTime to)
        {
            try
            {
                var collection = GetCollection<AuditLogModel>("IdpAuditLogs");
                var filter = Builders<AuditLogModel>.Filter.And(
                    Builders<AuditLogModel>.Filter.Eq(l => l.EventType, eventType),
                    Builders<AuditLogModel>.Filter.Gte(l => l.Timestamp, from),
                    Builders<AuditLogModel>.Filter.Lte(l => l.Timestamp, to)
                );
                return await collection.Find(filter).SortByDescending(l => l.Timestamp).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching audit logs by event type: {eventType}");
                throw;
            }
        }

        public async Task<IEnumerable<AuditLogModel>> GetBySeverityAsync(string severity, DateTime from, DateTime to)
        {
            try
            {
                var collection = GetCollection<AuditLogModel>("IdpAuditLogs");
                var filter = Builders<AuditLogModel>.Filter.And(
                    Builders<AuditLogModel>.Filter.Eq(l => l.Severity, severity),
                    Builders<AuditLogModel>.Filter.Gte(l => l.Timestamp, from),
                    Builders<AuditLogModel>.Filter.Lte(l => l.Timestamp, to)
                );
                return await collection.Find(filter).SortByDescending(l => l.Timestamp).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching audit logs by severity: {severity}");
                throw;
            }
        }

        public async Task<long> GetCountAsync(string eventType = null, DateTime? from = null, DateTime? to = null)
        {
            try
            {
                var collection = GetCollection<AuditLogModel>("IdpAuditLogs");
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting audit logs");
                throw;
            }
        }
    }
}

