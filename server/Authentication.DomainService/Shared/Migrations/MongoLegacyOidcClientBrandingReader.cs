using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Collections.Concurrent;

namespace Authentication.DomainService.Migrations
{
    /// <summary>
    /// Raw reader retained solely for the one-time ownership migration. Reading BSON avoids
    /// keeping the retired branding properties on <c>OidcClientRegistration</c>.
    /// </summary>
    public sealed class MongoLegacyOidcClientBrandingReader : ILegacyOidcClientBrandingReader
    {
        private const string CollectionName = "OidcClientRegistrations";
        private readonly ConcurrentDictionary<string, MongoClient> _clients = new();

        public async Task<IReadOnlyList<LegacyOidcClientBranding>> ReadAsync(
            string databaseName,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                throw new ArgumentException("Tenant database name is required.", nameof(databaseName));
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Tenant database connection string is required.", nameof(connectionString));
            }

            var client = _clients.GetOrAdd(connectionString, static value => new MongoClient(value));
            var collection = client.GetDatabase(databaseName).GetCollection<BsonDocument>(CollectionName);
            var projection = Builders<BsonDocument>.Projection
                .Include("ClientId")
                .Include("IsActive")
                .Include("LogoUri")
                .Include("UiBrandColor");

            var documents = await collection
                .Find(FilterDefinition<BsonDocument>.Empty)
                .Project(projection)
                .ToListAsync(cancellationToken);

            return documents
                .Select(document => BsonSerializer.Deserialize<LegacyOidcClientBranding>(document))
                .ToList();
        }
    }
}
