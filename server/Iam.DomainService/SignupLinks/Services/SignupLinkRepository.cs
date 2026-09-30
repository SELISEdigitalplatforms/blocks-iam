using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkRepository : ISignupLinkRepository
{
    public const string CollectionName = "SignupLinks";
    private const string CodeHashIndex = "ix_codehash_unique";
    private const string StatusExpiryIndex = "ix_tenant_status_expires";
    private const string ConfigurationIndex = "ix_tenant_configuration";
    private const string ConfigurationCreatedIndex = "ix_tenant_configuration_created";

    private readonly IIdentityAccessManagementRepository _iamRepository;
    private readonly ILogger<SignupLinkRepository> _logger;

    public SignupLinkRepository(
        IIdentityAccessManagementRepository iamRepository,
        ILogger<SignupLinkRepository> logger)
    {
        _iamRepository = iamRepository;
        _logger = logger;
    }

    private IMongoCollection<SignupLink> Collection =>
        _iamRepository.GetCollectionByName<SignupLink>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        try
        {
            // Drop Phase 2 prefix index; replaced by TenantId+ConfigurationId+CreatedDate.
            try
            {
                await Collection.Indexes.DropOneAsync(ConfigurationIndex, cancellationToken: ct);
            }
            catch (Exception dropEx)
            {
                _logger.LogDebug(dropEx,
                    "SignupLinkRepository: {Index} already absent or drop skipped.", ConfigurationIndex);
            }

            var keys = Builders<SignupLink>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<SignupLink>(
                    keys.Ascending(x => x.CodeHash),
                    new CreateIndexOptions { Name = CodeHashIndex, Unique = true }),
                new CreateIndexModel<SignupLink>(
                    keys.Ascending(x => x.TenantId).Ascending(x => x.Status).Ascending(x => x.ExpiresAtUtc),
                    new CreateIndexOptions { Name = StatusExpiryIndex }),
                new CreateIndexModel<SignupLink>(
                    keys.Ascending(x => x.TenantId).Ascending(x => x.ConfigurationId).Descending(x => x.CreatedDate),
                    new CreateIndexOptions { Name = ConfigurationCreatedIndex }),
            };
            await Collection.Indexes.CreateManyAsync(models, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignupLinkRepository.EnsureIndexesAsync failed; index creation is idempotent and will be retried.");
        }
    }

    public Task InsertAsync(SignupLink entity) =>
        Collection.InsertOneAsync(entity);

    public async Task<bool> ReplaceAsync(SignupLink entity)
    {
        var result = await Collection.ReplaceOneAsync(
            x => x.ItemId == entity.ItemId && x.TenantId == entity.TenantId,
            entity);
        return result.IsAcknowledged && result.MatchedCount > 0;
    }

    public async Task<SignupLink?> GetByIdAsync(string itemId, string tenantId)
    {
        var filter = Builders<SignupLink>.Filter.Eq(x => x.ItemId, itemId)
            & Builders<SignupLink>.Filter.Eq(x => x.TenantId, tenantId);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }



    public async Task<SignupLink?> GetByItemIdAsync(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        var filter = Builders<SignupLink>.Filter.Eq(x => x.ItemId, itemId);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<SignupLink?> GetByCodeHashAsync(string codeHash)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
        {
            return null;
        }

        var filter = Builders<SignupLink>.Filter.Eq(x => x.CodeHash, codeHash);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<SignupLink?> TryIncrementRedemptionAsync(
        string linkId,
        string tenantId,
        string createdUserId,
        DateTime nowUtc)
    {
        var filter = Builders<SignupLink>.Filter.Eq(x => x.ItemId, linkId)
            & Builders<SignupLink>.Filter.Eq(x => x.TenantId, tenantId)
            & Builders<SignupLink>.Filter.Eq(x => x.Status, SignupLinkStatus.Active)
            & Builders<SignupLink>.Filter.Gt(x => x.ExpiresAtUtc, nowUtc)
            & Builders<SignupLink>.Filter.Where(x => x.RedemptionCount < x.MaxRedemptions);

        var update = Builders<SignupLink>.Update
            .Inc(x => x.RedemptionCount, 1)
            .Set(x => x.Status, SignupLinkStatus.Redeemed)
            .Set(x => x.LastUpdatedDate, nowUtc)
            .Set(x => x.LastUpdatedBy, string.IsNullOrWhiteSpace(createdUserId) ? "signup-link" : createdUserId);

        // Only stamp CreatedUserId when this redemption created (or owns) the account.
        // Pre-existing org-join / redirect branches must not overwrite it (Phase 4 C1/H2).
        if (!string.IsNullOrWhiteSpace(createdUserId))
        {
            update = update.Set(x => x.CreatedUserId, createdUserId);
        }

        return await Collection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<SignupLink>
            {
                ReturnDocument = ReturnDocument.After
            });
    }

    public async Task<(List<SignupLink> Items, long TotalCount)> QueryAsync(
        string tenantId,
        QuerySignupLinksRequest request,
        SignupLinkScope? scope)
    {
        var filter = Builders<SignupLink>.Filter.Eq(x => x.TenantId, tenantId) & ScopeFilter(scope);
        if (!string.IsNullOrWhiteSpace(request.ConfigurationId))
        {
            filter &= Builders<SignupLink>.Filter.Eq(x => x.ConfigurationId, request.ConfigurationId);
        }

        var total = await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter)
            .Sort(Builders<SignupLink>.Sort.Descending(x => x.CreatedDate))
            .Skip(request.Page * request.PageSize)
            .Limit(request.PageSize)
            .ToListAsync();
        return (items, total);
    }

    public async Task<long> RevokeActiveByConfigurationAsync(
        string tenantId,
        string configurationId,
        string revokedBy,
        DateTime revokedAtUtc,
        SignupLinkScope? scope)
    {
        var filter = Builders<SignupLink>.Filter.Eq(x => x.TenantId, tenantId)
            & Builders<SignupLink>.Filter.Eq(x => x.ConfigurationId, configurationId)
            & Builders<SignupLink>.Filter.Eq(x => x.Status, SignupLinkStatus.Active)
            & ScopeFilter(scope);

        var update = Builders<SignupLink>.Update
            .Set(x => x.Status, SignupLinkStatus.Revoked)
            .Set(x => x.RevokedAtUtc, revokedAtUtc)
            .Set(x => x.RevokedBy, revokedBy)
            .Set(x => x.LastUpdatedDate, revokedAtUtc)
            .Set(x => x.LastUpdatedBy, revokedBy);

        var result = await Collection.UpdateManyAsync(filter, update);
        return result.ModifiedCount;
    }

    private static FilterDefinition<SignupLink> ScopeFilter(SignupLinkScope? scope)
    {
        if (scope is null)
        {
            return Builders<SignupLink>.Filter.Empty;
        }

        return Builders<SignupLink>.Filter.Eq(x => x.OrganizationId, scope.OrganizationId);
    }

    public async Task<List<SignupLink>> FindForSummaryAsync(
        string tenantId,
        string configurationId,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var filter = Builders<SignupLink>.Filter.Eq(x => x.TenantId, tenantId)
            & Builders<SignupLink>.Filter.Eq(x => x.ConfigurationId, configurationId)
            & Builders<SignupLink>.Filter.Gte(x => x.CreatedDate, fromUtc)
            & Builders<SignupLink>.Filter.Lt(x => x.CreatedDate, toUtc);

        return await Collection.Find(filter).ToListAsync();
    }
}
