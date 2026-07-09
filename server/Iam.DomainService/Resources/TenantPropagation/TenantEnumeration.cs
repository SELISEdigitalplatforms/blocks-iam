using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Resources.TenantPropagation
{
    public class TenantEnumeration : ITenantEnumeration
    {
        public const string TenantsCollectionName = "Tenants";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        private readonly IMongoDatabase _rootDatabase;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TenantEnumeration> _logger;

        public TenantEnumeration(
            IMongoDatabase rootDatabase,
            IMemoryCache cache,
            ILogger<TenantEnumeration> logger)
        {
            _rootDatabase = rootDatabase;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IReadOnlyList<PermissionMutationTarget>> GetTargetsAsync(string? excludeTenantId)
        {
            var cacheKey = string.IsNullOrWhiteSpace(excludeTenantId) ? "_all" : excludeTenantId;
            var envelope = _cache.Get<CacheEntry>(cacheKey);
            if (envelope is not null && envelope.LoadedAt + CacheTtl > DateTime.UtcNow)
            {
                return envelope.Targets;
            }

            try
            {
                var collection = _rootDatabase.GetCollection<BsonDocument>(TenantsCollectionName);

                var filter = Builders<BsonDocument>.Filter.Eq("IsDisabled", false);
                if (!string.IsNullOrWhiteSpace(excludeTenantId))
                {
                    filter &= Builders<BsonDocument>.Filter.Ne("TenantId", excludeTenantId);
                }

                var docs = await collection.Find(filter).ToListAsync();
                var targets = new List<PermissionMutationTarget>(docs.Count);
                foreach (var doc in docs)
                {
                    var tenantId = doc.GetValueOrDefault("TenantId", string.Empty);
                    if (string.IsNullOrWhiteSpace(tenantId))
                    {
                        continue;
                    }

                    var connString = doc.GetValueOrDefault("DbConnectionString", string.Empty);
                    var dbName = doc.GetValueOrDefault("DBName", string.Empty);
                    if (string.IsNullOrWhiteSpace(connString) || string.IsNullOrWhiteSpace(dbName))
                    {
                        continue;
                    }

                    targets.Add(new PermissionMutationTarget
                    {
                        TenantId = tenantId,
                        TenantName = doc.GetValueOrDefault("Name", string.Empty),
                        DbConnectionString = connString,
                        DBName = dbName
                    });
                }

                _logger.LogDebug(
                    "Enumerated {Count} enabled tenants for propagation (excluded={Excluded})",
                    targets.Count, excludeTenantId ?? "<none>");

                _cache.Set(cacheKey, new CacheEntry(targets, DateTime.UtcNow), CacheTtl);
                return targets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate tenants from root DB (Tenants collection).");
                return envelope?.Targets ?? Array.Empty<PermissionMutationTarget>();
            }
        }

        private sealed record CacheEntry(IReadOnlyList<PermissionMutationTarget> Targets, DateTime LoadedAt);
    }

    internal static class BsonDocumentExtensions
    {
        public static string GetValueOrDefault(this BsonDocument doc, string name, string fallback)
        {
            return doc.TryGetValue(name, out var v) && v is BsonString bs ? bs.AsString : fallback;
        }
    }
}
