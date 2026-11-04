using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkConfigurationRepository : ISignupLinkConfigurationRepository
{
    public const string CollectionName = "SignupLinkConfigurations";
    private const string NameIndex = "ix_tenant_name_unique";
    private const string ActiveIndex = "ix_tenant_active";

    private readonly IIdentityAccessManagementRepository _iamRepository;
    private readonly ILogger<SignupLinkConfigurationRepository> _logger;
    private readonly IIamDataAccess _dataAccess;

    public SignupLinkConfigurationRepository(
        IIdentityAccessManagementRepository iamRepository,
        ILogger<SignupLinkConfigurationRepository> logger,
        IIamDataAccess dataAccess)
    {
        _iamRepository = iamRepository;
        _logger = logger;
        _dataAccess = dataAccess;
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
        _dataAccess.InsertOneAsync(Collection, IamSchemas.SignupLinkConfiguration, entity, "SignupLinkConfigurationRepository.InsertAsync");

    public async Task<bool> ReplaceAsync(SignupLinkConfiguration entity)
    {
        var result = await _dataAccess.ReplaceOneAsync(
            Collection,
            IamSchemas.SignupLinkConfiguration,
            Builders<SignupLinkConfiguration>.Filter.Where(x => x.ItemId == entity.ItemId && x.TenantId == entity.TenantId),
            entity,
            "SignupLinkConfigurationRepository.ReplaceAsync");
        return result is not null && result.IsAcknowledged && result.MatchedCount > 0;
    }

    public async Task<SignupLinkConfiguration?> GetByIdAsync(string itemId, string tenantId)
    {
        var filter = Builders<SignupLinkConfiguration>.Filter.Eq(x => x.ItemId, itemId)
            & Builders<SignupLinkConfiguration>.Filter.Eq(x => x.TenantId, tenantId);
        return await _dataAccess.FindFirstAsync(Collection, IamSchemas.SignupLinkConfiguration, filter, "SignupLinkConfigurationRepository.GetByIdAsync");
    }

    public async Task<SignupLinkConfiguration?> GetByIdForViewAsync(string itemId, string tenantId)
    {
        var filter = Builders<SignupLinkConfiguration>.Filter.Eq(x => x.ItemId, itemId)
            & Builders<SignupLinkConfiguration>.Filter.Eq(x => x.TenantId, tenantId);
        return await _dataAccess.FindFirstForViewAsync(Collection, IamSchemas.SignupLinkConfiguration, filter, "SignupLinkConfigurationRepository.GetByIdForViewAsync");
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
        var scope = await _dataAccess.ReadAsync(IamSchemas.SignupLinkConfiguration, filter, "SignupLinkConfigurationRepository.FindByNameAsync", useProjection: false);
        if (!scope.Granted) return null;
        return await (await Collection.FindAsync(scope.Filter, options)).FirstOrDefaultAsync();
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

        var scope = await _dataAccess.ReadAsync(IamSchemas.SignupLinkConfiguration, filter, "SignupLinkConfigurationRepository.QueryAsync", useProjection: false);
        if (!scope.Granted) return ([], 0);
        var total = await Collection.CountDocumentsAsync(scope.Filter);
        var items = await scope.Apply(Collection)
            .Sort(Builders<SignupLinkConfiguration>.Sort.Descending(x => x.CreatedDate))
            .Skip(request.Page * request.PageSize)
            .Limit(request.PageSize)
            .ToListAsync();
        return (scope.ForView(items), total);
    }
}
