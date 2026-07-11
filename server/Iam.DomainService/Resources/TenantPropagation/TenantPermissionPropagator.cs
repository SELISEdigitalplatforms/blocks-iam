using System.Collections.Concurrent;
using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Resources.TenantPropagation
{
    public class TenantPermissionPropagator : ITenantPermissionPropagator
    {
        public const string PermissionsCollectionName = "Permissions";

        private const int MaxConcurrentTenants = 8;
        
        private readonly ConcurrentDictionary<string, MongoClient> _clients = new();
        private readonly IResourceRepository _resourceRepository;
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<TenantPermissionPropagator> _logger;

        public TenantPermissionPropagator(
            IResourceRepository resourceRepository,
            IDbContextProvider dbContextProvider,
            ILogger<TenantPermissionPropagator> logger)
        {
            _resourceRepository = resourceRepository;
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        public async Task<PropagationSummary> PropagateAsync(PermissionMutationForTenantsEvent context)
        {
            var summary = new PropagationSummary
            {
                PermissionItemId = context.ItemId,
                Action = context.Action
            };

            var source = await _resourceRepository.GetPermissionByIdAsync(context.ItemId);
            if (source is null)
            {
                _logger.LogInformation(
                    "Propagation skipped: permission not found. ItemId={ItemId}",
                    context.ItemId);
                return summary;
            }

            if (!source.IsBuiltIn)
            {
                _logger.LogInformation(
                    "Propagation skipped: permission is not built-in. ItemId={ItemId} Resource={Resource}",
                    context.ItemId, source.Resource);
                return summary;
            }

            var blocksContext = BlocksContext.GetContext();
            var sourceTenantId = blocksContext?.TenantId;
            if (string.IsNullOrWhiteSpace(sourceTenantId))
            {
                _logger.LogWarning(
                    "Propagation skipped: missing source TenantId in BlocksContext. ItemId={ItemId}",
                    context.ItemId);
                return summary;
            }

            var tenants = await GetTargetsAsync(sourceTenantId);
            if (tenants.Count == 0)
            {
                _logger.LogInformation(
                    "Propagation: no enabled non-source tenants found. ItemId={ItemId}",
                    context.ItemId);
                return summary;
            }

            summary.TenantsAttempted = tenants.Count;
            var results = new ConcurrentBag<TenantPropagationResult>();
            using var semaphore = new SemaphoreSlim(MaxConcurrentTenants);

            var tasks = tenants.Select(target => ProcessTenantAsync(source, context.Action, target, semaphore, results));
            await Task.WhenAll(tasks);

            summary.Results = results.ToList();
            summary.TenantsSucceeded = summary.Results.Count(r => r.Success);
            summary.TenantsFailed = summary.Results.Count(r => !r.Success);

            _logger.LogInformation(
                "Propagation complete. ItemId={ItemId} Action={Action} Resource={Resource} Attempted={Attempted} Succeeded={Succeeded} Failed={Failed}",
                context.ItemId, context.Action, source.Resource, summary.TenantsAttempted, summary.TenantsSucceeded, summary.TenantsFailed);

            return summary;
        }

        public async Task<IReadOnlyList<PermissionMutationTarget>> GetTargetsAsync(string? excludeTenantId)
        {
            try
            {
                var collection = _dbContextProvider.GetCollection<Tenant>("Tenants");

                var filter = Builders<Tenant>.Filter.Eq("IsDisabled", false);

                var docs = await collection.Find(filter).ToListAsync();
                var targets = new List<PermissionMutationTarget>(docs.Count);
                foreach (var doc in docs)
                {
                    var tenantId = doc.TenantId;
                    if (string.IsNullOrWhiteSpace(tenantId) || doc.IsDisabled)
                    {
                        continue;
                    }

                    if(doc.IsRootTenant)
                    {
                        targets.Add(new PermissionMutationTarget
                        {
                            TenantId = tenantId,
                            TenantName = doc.Name,
                            DbConnectionString = doc.DbConnectionString,
                            DBName = "BlocksConfiguration"
                        });

                        continue;
                    }

                    var connString = doc.DbConnectionString;
                    var dbName = doc.DBName;
                    if (string.IsNullOrWhiteSpace(connString) || string.IsNullOrWhiteSpace(dbName))
                    {
                        continue;
                    }

                    targets.Add(new PermissionMutationTarget
                    {
                        TenantId = tenantId,
                        TenantName = doc.Name,
                        DbConnectionString = connString,
                        DBName = dbName
                    });
                }

                _logger.LogDebug(
                    "Enumerated {Count} enabled tenants for propagation (excluded={Excluded})",
                    targets.Count, excludeTenantId ?? "<none>");

                return targets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate tenants from root DB (Tenants collection).");
                return Array.Empty<PermissionMutationTarget>();
            }
        }

        private async Task ProcessTenantAsync(
            Permission source,
            MutationEventType action,
            PermissionMutationTarget target,
            SemaphoreSlim semaphore,
            ConcurrentBag<TenantPropagationResult> results)
        {
            await semaphore.WaitAsync();
            try
            {
                var database = OpenDatabase(target.DbConnectionString, target.DBName);
                var collection = database.GetCollection<BsonDocument>(PermissionsCollectionName);
                var resourceFilter = Builders<BsonDocument>.Filter.Eq("Resource", source.Resource);

                long affected;
                switch (action)
                {
                    case MutationEventType.Create:
                        affected = await ApplyInsertAsync(collection, source, database);
                        break;
                    case MutationEventType.Update:
                        affected = await ApplyUpdateAsync(collection, source);
                        break;
                    case MutationEventType.Delete:
                        var archive = Builders<BsonDocument>.Update.Set("IsArchived", true);
                        var archiveResult = await collection.UpdateManyAsync(resourceFilter, archive);
                        affected = archiveResult.IsAcknowledged ? archiveResult.ModifiedCount : 0;
                        break;
                    default:
                        _logger.LogDebug(
                            "Skipping tenant {TenantId}: unsupported action {Action}",
                            target.TenantId, action);
                        return;
                }

                results.Add(new TenantPropagationResult
                {
                    TenantId = target.TenantId,
                    TenantName = target.TenantName ?? string.Empty,
                    Success = true,
                    DocumentsAffected = affected
                });
            }
            catch (Exception ex)
            {
                LogTenantFailure(target.TenantId, source.ItemId, action, ex);
                results.Add(new TenantPropagationResult
                {
                    TenantId = target.TenantId,
                    TenantName = target.TenantName ?? string.Empty,
                    Success = false,
                    ErrorMessage = ex.Message,
                    ErrorType = ex.GetType().Name
                });
            }
            finally
            {
                semaphore.Release();
            }
        }

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

        private async Task<long> ApplyInsertAsync(
            IMongoCollection<BsonDocument> collection,
            Permission permission,
            IMongoDatabase database)
        {
            try
            {
                var payload = BuildPermissionBson(permission);
                await collection.InsertOneAsync(payload);
                var configCollection = database.GetCollection<TenantConfiguration>("TenantConfigurations");
                var tenantConfiguration = await configCollection.Find(_ => true).FirstOrDefaultAsync();
                if (tenantConfiguration == null || !tenantConfiguration.IsMultiOrgEnabled)
                {
                    return 1;
                }

                var orgIds = (await database.GetCollection<Organization>("Organizations").Find(_ => true).ToListAsync())?.Select(x => x.ItemId).ToList() ?? [];

                if (!orgIds.Any())
                {
                    _logger.LogWarning("Organizations are empty");
                    return 1;
                }

                var permissions = orgIds.Select(orgId => BuildPermissionBson(permission, orgId)).ToList();

                await collection.InsertManyAsync(permissions);

                _logger.LogInformation(
                    "Created permission '{Resource}' for {Count} organizations",
                    permission.Resource,
                    permissions.Count);

                return permissions.Count;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return 0;
            }
        }

        private async Task<long> ApplyUpdateAsync(
            IMongoCollection<BsonDocument> collection,
            Permission source)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("Resource", source.Resource);
            var update = Builders<BsonDocument>.Update
                .Set("Name", source.Name ?? string.Empty)
                .Set("Description", source.Description ?? string.Empty)
                .Set("Type", source.Type.ToString())
                .Set("Resource", source.Resource)
                .Set("ResourceGroup", source.ResourceGroup ?? string.Empty)
                .Set("IsBuiltIn", source.IsBuiltIn)
                .Set("IsArchived", source.IsArchived)
                .Set("PermissionSeverity", source.PermissionSeverity.ToString())
                .Set("Tags", new BsonArray(source.Tags ?? new List<string>()))
                .Set("DependentPermissions", new BsonArray(source.DependentPermissions ?? new List<string>()));

            var result = await collection.UpdateManyAsync(filter, update);
            return result.IsAcknowledged ? result.ModifiedCount : 0;
        }

        private static BsonDocument BuildPermissionBson(Permission source, string orgId="default")
        {
            var now = DateTime.UtcNow;
            return new BsonDocument
            {
                { "_id", source.ItemId },
                { "ItemId", source.ItemId },
                { "Name", source.Name ?? string.Empty },
                { "Description", source.Description ?? string.Empty },
                { "Type", source.Type.ToString() },
                { "Resource", source.Resource },
                { "ResourceGroup", source.ResourceGroup ?? string.Empty },
                { "IsBuiltIn", source.IsBuiltIn },
                { "IsArchived", source.IsArchived },
                { "PermissionSeverity", source.PermissionSeverity.ToString() },
                { "Tags", new BsonArray(source.Tags ?? new List<string>()) },
                { "DependentPermissions", new BsonArray(source.DependentPermissions ?? new List<string>()) },
                { "OrganizationId", orgId },
                { "Roles", new BsonArray() },
                { "CreatedBy", source.CreatedBy ?? string.Empty },
                { "CreatedDate", source.CreatedDate == default ? now : source.CreatedDate },
                { "LastUpdatedBy", source.LastUpdatedBy ?? string.Empty },
                { "LastUpdatedDate", source.LastUpdatedDate == default ? now : source.LastUpdatedDate }
            };
        }

        private void LogTenantFailure(string tenantId, string itemId, MutationEventType action, Exception ex)
        {
            _logger.LogWarning(
                "Permission propagation failed. TenantId={TenantId} PermissionItemId={ItemId} Action={Action} ErrorType={ErrorType} ErrorMessage={Message}",
                tenantId, itemId, action, ex.GetType().Name, ex.Message);
        }
    }
}
