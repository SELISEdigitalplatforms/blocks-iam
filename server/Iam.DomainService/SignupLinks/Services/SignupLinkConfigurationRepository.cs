using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkConfigurationRepository : ISignupLinkConfigurationRepository
{
    public const string CollectionName = "SignupLinkConfigurations";
    private const string NameIndex = "ix_tenant_name_unique";
    private const string ActiveIndex = "ix_tenant_active";

    private readonly IIdentityAccessManagementRepository _iamRepository;
    private readonly ILogger<SignupLinkConfigurationRepository> _logger;

    public SignupLinkConfigurationRepository(
        IIdentityAccessManagementRepository iamRepository,
        ILogger<SignupLinkConfigurationRepository> logger)
    {
        _iamRepository = iamRepository;
        _logger = logger;
    }

    private IMongoCollection<SignupLinkConfiguration> Collection =>
        _iamRepository.GetCollectionByName<SignupLinkConfiguration>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        try
        {
            var keys = Builders<SignupLinkConfiguration>.IndexKeys;
            var nameModel = new CreateIndexModel<SignupLinkConfiguration>(
                keys.Ascending(x => x.TenantId).Ascending(x => x.Name),
                new CreateIndexOptions<SignupLinkConfiguration>
                {
                    Name = NameIndex,
                    Unique = true,
                    Collation = new Collation("en", strength: CollationStrength.Secondary)
                });
            var activeModel = new CreateIndexModel<SignupLinkConfiguration>(
                keys.Ascending(x => x.TenantId).Ascending(x => x.IsActive),
                new CreateIndexOptions { Name = ActiveIndex });

            await Collection.Indexes.CreateManyAsync([nameModel, activeModel], cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignupLinkConfigurationRepository.EnsureIndexesAsync failed; index creation is idempotent and will be retried.");
        }
    }

    public Task InsertAsync(SignupLinkConfiguration entity) =>
        Collection.InsertOneAsync(entity);

    public async Task<bool> ReplaceAsync(SignupLinkConfiguration entity)
    {
        var result = await Collection.ReplaceOneAsync(
            x => x.ItemId == entity.ItemId && x.TenantId == entity.TenantId,
            entity);
        return result.IsAcknowledged && result.MatchedCount > 0;
    }

    public async Task<SignupLinkConfiguration?> GetByIdAsync(string itemId, string tenantId)
    {
        var filter = Builders<SignupLinkConfiguration>.Filter.Eq(x => x.ItemId, itemId)
            & Builders<SignupLinkConfiguration>.Filter.Eq(x => x.TenantId, tenantId);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<SignupLinkConfiguration?> FindByNameAsync(string tenantId, string name, string? excludeItemId = null)
    {
        var filter = Builders<SignupLinkConfiguration>.Filter.Eq(x => x.TenantId, tenantId)
            & Builders<SignupLinkConfiguration>.Filter.Eq(x => x.Name, name);
        if (!string.IsNullOrWhiteSpace(excludeItemId))
        {
            filter &= Builders<SignupLinkConfiguration>.Filter.Ne(x => x.ItemId, excludeItemId);
        }

        var options = new FindOptions<SignupLinkConfiguration>
        {
            Collation = new Collation("en", strength: CollationStrength.Secondary),
            Limit = 1
        };
        return await (await Collection.FindAsync(filter, options)).FirstOrDefaultAsync();
    }

    public async Task<(List<SignupLinkConfiguration> Items, long TotalCount)> QueryAsync(
        string tenantId,
        QuerySignupLinkConfigurationsRequest request)
    {
        var filter = Builders<SignupLinkConfiguration>.Filter.Eq(x => x.TenantId, tenantId);
        if (!request.IncludeInactive)
        {
            filter &= Builders<SignupLinkConfiguration>.Filter.Eq(x => x.IsActive, true);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            filter &= Builders<SignupLinkConfiguration>.Filter.Regex(
                x => x.Name,
                new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(search), "i"));
        }

        var total = await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter)
            .Sort(Builders<SignupLinkConfiguration>.Sort.Descending(x => x.CreatedDate))
            .Skip(request.Page * request.PageSize)
            .Limit(request.PageSize)
            .ToListAsync();
        return (items, total);
    }
}
