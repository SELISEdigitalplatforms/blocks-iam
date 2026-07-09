using System.Collections.Concurrent;
using MongoDB.Driver;

namespace Iam.DomainService.Resources.TenantPropagation
{
    /// <summary>
    /// Caches <see cref="MongoClient"/> per tenant connection string. Drivers are
    /// thread-safe, so caching avoids reconnect churn during fan-out.
    /// <para>
    /// Per-tenant connection strings and database names are resolved at publisher
    /// time and embedded into the queued event, so this factory only opens arbitrary
    /// tenant databases. Root DB access is wired separately via
    /// <see cref="Iam.DomainService.Shared.Services.IRootDatabaseProvider"/>.
    /// </para>
    /// </summary>
    public class TenantConnectionFactory
    {
        public const string PermissionsCollectionName = "Permissions";

        private readonly ConcurrentDictionary<string, MongoClient> _clients = new();

        public IMongoDatabase OpenDatabase(string connectionString, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Tenant connection string is required", nameof(connectionString));
            }

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                throw new ArgumentException("Tenant database name is required", nameof(databaseName));
            }

            var client = _clients.GetOrAdd(connectionString, static cs => new MongoClient(cs));
            return client.GetDatabase(databaseName);
        }
    }
}
