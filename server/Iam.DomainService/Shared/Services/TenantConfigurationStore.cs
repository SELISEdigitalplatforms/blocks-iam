using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Services
{
    /// <summary>
    /// Reads and writes the tenant configuration (signup and organization settings).
    /// </summary>
    /// <remarks>
    /// The configuration lives in the field <c>TenantConfiguration</c> of the one
    /// <c>IdentityConfigurations</c> document. Until a database has that field (before the migration, or a
    /// database without an <c>IdentityConfigurations</c> document) the old <c>TenantConfigurations</c>
    /// collection is read and written, exactly as before. Every read and write of the configuration goes
    /// through this class.
    /// </remarks>
    public class TenantConfigurationStore
    {
        public const string FieldName = "TenantConfiguration";
        public const string IdentityConfigurationCollection = "IdentityConfigurations";
        public const string LegacyCollection = "TenantConfigurations";
        private const string LastUpdatedDate = "LastUpdatedDate";

        private readonly IDbContextProvider _dbContextProvider;

        public TenantConfigurationStore(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        /// <summary>Reads the configuration of the context tenant's database.</summary>
        public Task<TenantConfiguration?> ReadAsync(CancellationToken ct = default) =>
            ReadAsync(
                _dbContextProvider.GetCollection<BsonDocument>(IdentityConfigurationCollection),
                _dbContextProvider.GetCollection<TenantConfiguration>(LegacyCollection),
                ct);

        /// <summary>Reads the configuration of another tenant's database (cross-database read).</summary>
        public Task<TenantConfiguration?> ReadAsync(IMongoDatabase database, CancellationToken ct = default) =>
            ReadAsync(
                database.GetCollection<BsonDocument>(IdentityConfigurationCollection),
                database.GetCollection<TenantConfiguration>(LegacyCollection),
                ct);

        /// <summary>
        /// Applies <paramref name="update"/> (written against <see cref="TenantConfiguration"/>, as it was for the
        /// old collection) to the field <c>TenantConfiguration</c> and sets its <c>LastUpdatedDate</c>. When the
        /// database has no such field, runs the same update on the old collection, unchanged. Returns whether the
        /// write was acknowledged.
        /// </summary>
        public Task<bool> UpdateAsync(
            UpdateDefinition<TenantConfiguration> update,
            bool upsertLegacy = false,
            CancellationToken ct = default) =>
            UpdateAsync(
                _dbContextProvider.GetCollection<BsonDocument>(IdentityConfigurationCollection),
                _dbContextProvider.GetCollection<TenantConfiguration>(LegacyCollection),
                update,
                upsertLegacy,
                ct);

        private static async Task<TenantConfiguration?> ReadAsync(
            IMongoCollection<BsonDocument> identity,
            IMongoCollection<TenantConfiguration> legacy,
            CancellationToken ct)
        {
            var first = await identity
                .Find(Builders<BsonDocument>.Filter.Empty)
                .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include(FieldName).Exclude("_id"))
                .FirstOrDefaultAsync(ct);

            if (first is not null
                && first.TryGetValue(FieldName, out var value)
                && value.IsBsonDocument)
            {
                return BsonSerializer.Deserialize<TenantConfiguration>(value.AsBsonDocument);
            }

            return await legacy.Find(_ => true).FirstOrDefaultAsync(ct);
        }

        private static async Task<bool> UpdateAsync(
            IMongoCollection<BsonDocument> identity,
            IMongoCollection<TenantConfiguration> legacy,
            UpdateDefinition<TenantConfiguration> update,
            bool upsertLegacy,
            CancellationToken ct)
        {
            var result = await identity.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Type(FieldName, BsonType.Document),
                ToFieldUpdate(update),
                cancellationToken: ct);

            if (result.MatchedCount > 0)
            {
                return result.IsAcknowledged;
            }

            var legacyResult = await legacy.UpdateOneAsync(
                Builders<TenantConfiguration>.Filter.Empty,
                update,
                upsertLegacy ? new UpdateOptions { IsUpsert = true } : null,
                ct);
            return legacyResult?.IsAcknowledged ?? false;
        }

        /// <summary>
        /// Renders <paramref name="update"/> with the same field names and values the old collection got,
        /// moves every path under <c>TenantConfiguration.</c>, and sets <c>LastUpdatedDate</c> to now.
        /// <c>$setOnInsert</c> is dropped: the field exists (the filter), so nothing is inserted.
        /// </summary>
        private static UpdateDefinition<BsonDocument> ToFieldUpdate(UpdateDefinition<TenantConfiguration> update)
        {
            var rendered = update
                .Render(new RenderArgs<TenantConfiguration>(
                    BsonSerializer.SerializerRegistry.GetSerializer<TenantConfiguration>(),
                    BsonSerializer.SerializerRegistry))
                .AsBsonDocument;

            var result = new BsonDocument();
            foreach (var op in rendered.Elements)
            {
                if (op.Name == "$setOnInsert")
                {
                    continue;
                }

                var moved = new BsonDocument();
                foreach (var field in op.Value.AsBsonDocument.Elements)
                {
                    moved.Add($"{FieldName}.{field.Name}", field.Value);
                }

                result.Add(op.Name, moved);
            }

            if (!result.Contains("$set"))
            {
                result.Add("$set", new BsonDocument());
            }

            result["$set"].AsBsonDocument[$"{FieldName}.{LastUpdatedDate}"] = DateTime.UtcNow;

            return new BsonDocumentUpdateDefinition<BsonDocument>(result);
        }
    }
}
