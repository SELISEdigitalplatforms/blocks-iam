using System.Collections.Concurrent;
using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Resources.TenantPropagation
{
    public class TenantPermissionPropagator : ITenantPermissionPropagator
    {
        private const int MaxConcurrentTenants = 8;

        private readonly IResourceRepository _resourceRepository;
        private readonly ITenantEnumeration _tenantEnumeration;
        private readonly TenantConnectionFactory _connectionFactory;
        private readonly ILogger<TenantPermissionPropagator> _logger;

        public TenantPermissionPropagator(
            IResourceRepository resourceRepository,
            ITenantEnumeration tenantEnumeration,
            TenantConnectionFactory connectionFactory,
            ILogger<TenantPermissionPropagator> logger)
        {
            _resourceRepository = resourceRepository;
            _tenantEnumeration = tenantEnumeration;
            _connectionFactory = connectionFactory;
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

            var tenants = await _tenantEnumeration.GetTargetsAsync(sourceTenantId);
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
                var database = _connectionFactory.OpenDatabase(target.DbConnectionString, target.DBName);
                var collection = database.GetCollection<BsonDocument>(TenantConnectionFactory.PermissionsCollectionName);
                var resourceFilter = Builders<BsonDocument>.Filter.Eq("Resource", source.Resource);

                long affected;
                switch (action)
                {
                    case MutationEventType.Create:
                        affected = await ApplyInsertAsync(collection, source);
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

        private static async Task<long> ApplyInsertAsync(
            IMongoCollection<BsonDocument> collection,
            Permission source)
        {
            var payload = BuildPermissionBson(source);
            try
            {
                await collection.InsertOneAsync(payload);
                return 1;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return 0;
            }
        }

        private static async Task<long> ApplyUpdateAsync(
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

        private static BsonDocument BuildPermissionBson(Permission source)
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
                { "OrganizationId", "default" },
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
