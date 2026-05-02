using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Blocks.Genesis.Auth;
using Microsoft.Extensions.Logging;

namespace DomainService.Oidc.Repositories
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly IMongoDatabase _database;
        private readonly ILogger<AuditLogRepository> _logger;

        public AuditLogRepository(IMongoDatabase database, ILogger<AuditLogRepository> logger)
        {
            _database = database;
            _logger = logger;
        }

        public async Task<string> CreateAsync(AuditLogModel log)
        {
            try
            {
                var collection = _database.GetCollection<AuditLogModel>("audit_logs");
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
                var collection = _database.GetCollection<AuditLogModel>("audit_logs");
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
                var collection = _database.GetCollection<AuditLogModel>("audit_logs");
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
                var collection = _database.GetCollection<AuditLogModel>("audit_logs");
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
                var collection = _database.GetCollection<AuditLogModel>("audit_logs");
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

