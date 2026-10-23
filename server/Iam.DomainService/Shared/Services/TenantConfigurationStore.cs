using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Services
{
    /// <summary>
    /// Reads and writes the tenant configuration (signup and organization settings). Reads always return an
    /// <see cref="IamTenantConfiguration"/>, which carries the IAM-only settings next to the Genesis entity's.
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
        private static readonly FilterDefinition<BsonDocument> HasField = Builders<BsonDocument>.Filter.Type(FieldName, BsonType.Document);

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IIamDataAccess? _dataAccess;

        /// <summary>No data access gate: only for the cross-database read (another tenant's DB; the resolver loads the caller's tenant policies only).</summary>
        public TenantConfigurationStore(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public TenantConfigurationStore(IDbContextProvider dbContextProvider, IIamDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }

        /// <summary>Reads the configuration of the context tenant's database.</summary>
        public Task<IamTenantConfiguration?> ReadAsync(CancellationToken ct = default) =>
            ReadAsync(
                _dbContextProvider.GetCollection<BsonDocument>(IdentityConfigurationCollection),
                _dbContextProvider.GetCollection<IamTenantConfiguration>(LegacyCollection),
                _dataAccess,
                ct);

        /// <summary>Reads the configuration of another tenant's database (cross-database read).</summary>
        public static Task<IamTenantConfiguration?> ReadAsync(IMongoDatabase database, CancellationToken ct = default) =>
            ReadAsync(
                database.GetCollection<BsonDocument>(IdentityConfigurationCollection),
                database.GetCollection<IamTenantConfiguration>(LegacyCollection),
                null, // skip-other-db: the database is not the caller's tenant DB
                ct);

        /// <summary>
        /// Applies <paramref name="update"/> (written against <see cref="TenantConfiguration"/> or
        /// <see cref="IamTenantConfiguration"/>, as it was for the old collection) to the field
        /// <c>TenantConfiguration</c> and sets its <c>LastUpdatedDate</c>. When the database has no such field, runs
        /// the same update on the old collection, unchanged. Returns whether the write was acknowledged; false
        /// when the data access policy denies the edit or hides the identity document.
        /// </summary>
        public Task<bool> UpdateAsync<TConfiguration>(
            UpdateDefinition<TConfiguration> update,
            bool upsertLegacy = false,
            CancellationToken ct = default)
            where TConfiguration : TenantConfiguration =>
            UpdateAsync(
                _dbContextProvider.GetCollection<BsonDocument>(IdentityConfigurationCollection),
                _dbContextProvider.GetCollection<TConfiguration>(LegacyCollection),
                update,
                upsertLegacy,
                _dataAccess,
                ct);

        private static async Task<IamTenantConfiguration?> ReadAsync(
            IMongoCollection<BsonDocument> identity,
            IMongoCollection<IamTenantConfiguration> legacy,
            IIamDataAccess? dataAccess,
            CancellationToken ct)
        {
            FilterDefinition<BsonDocument> filter = Builders<BsonDocument>.Filter.Empty;
            if (dataAccess is not null)
            {
                var scope = await dataAccess.ReadAsync(IamSchemas.IdentityConfiguration, filter, "TenantConfigurationStore.ReadAsync", useProjection: false);
                if (!scope.Granted) return null;
                filter = scope.Filter;
            }

            var first = await identity
                .Find(filter)
                .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include(FieldName).Exclude("_id"))
                .FirstOrDefaultAsync(ct);

            if (first is not null
                && first.TryGetValue(FieldName, out var value)
                && value.IsBsonDocument)
            {
                return BsonSerializer.Deserialize<IamTenantConfiguration>(value.AsBsonDocument);
            }

            if (dataAccess is not null && !await LegacyInScopeAsync(identity, filter, ct))
            {
                return null;
            }

            return await legacy.Find(_ => true).FirstOrDefaultAsync(ct);
        }

        private static async Task<bool> UpdateAsync<TConfiguration>(
            IMongoCollection<BsonDocument> identity,
            IMongoCollection<TConfiguration> legacy,
            UpdateDefinition<TConfiguration> update,
            bool upsertLegacy,
            IIamDataAccess? dataAccess,
            CancellationToken ct)
        {
            FilterDefinition<BsonDocument> scope = Builders<BsonDocument>.Filter.Empty;
            var fieldUpdate = ToFieldUpdate(update);
            if (dataAccess is not null)
            {
                var edit = await dataAccess.EditFilterAsync(IamSchemas.IdentityConfiguration, scope, "TenantConfigurationStore.UpdateAsync");
                if (edit is null) return false;
                await dataAccess.CheckUpdateAsync(IamSchemas.IdentityConfiguration, fieldUpdate, "TenantConfigurationStore.UpdateAsync");
                scope = edit;
            }

            var result = await identity.UpdateOneAsync(
                Builders<BsonDocument>.Filter.And(scope, HasField),
                fieldUpdate,
                cancellationToken: ct);

            if (result.MatchedCount > 0)
            {
                return result.IsAcknowledged;
            }

            if (dataAccess is not null && !await LegacyInScopeAsync(identity, scope, ct))
            {
                return false;
            }

            var legacyResult = await legacy.UpdateOneAsync(
                Builders<TConfiguration>.Filter.Empty,
                update,
                upsertLegacy ? new UpdateOptions { IsUpsert = true } : null,
                ct);
            return legacyResult?.IsAcknowledged ?? false;
        }

        /// <summary>
        /// The old collection stands in for the field, so it gets the identity document's access: only while no
        /// identity document has the field (checked without the row filter), and only when the caller's
        /// <paramref name="scope"/> sees the identity document or there is none.
        /// </summary>
        private static async Task<bool> LegacyInScopeAsync(
            IMongoCollection<BsonDocument> identity,
            FilterDefinition<BsonDocument> scope,
            CancellationToken ct)
        {
            if (await identity.Find(HasField).Limit(1).AnyAsync(ct))
            {
                return false;
            }

            return !await identity.Find(Builders<BsonDocument>.Filter.Empty).Limit(1).AnyAsync(ct)
                || await identity.Find(scope).Limit(1).AnyAsync(ct);
        }

        /// <summary>
        /// Renders <paramref name="update"/> with the same field names and values the old collection got,
        /// moves every path under <c>TenantConfiguration.</c>, and sets <c>LastUpdatedDate</c> to now.
        /// <c>$setOnInsert</c> is dropped: the field exists (the filter), so nothing is inserted.
        /// </summary>
        private static UpdateDefinition<BsonDocument> ToFieldUpdate<TConfiguration>(UpdateDefinition<TConfiguration> update)
        {
            var rendered = update
                .Render(new RenderArgs<TConfiguration>(
                    BsonSerializer.SerializerRegistry.GetSerializer<TConfiguration>(),
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
