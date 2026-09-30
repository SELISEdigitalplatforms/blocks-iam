using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkRedemptionRepository : ISignupLinkRedemptionRepository
{
    public const string CollectionName = "SignupLinkRedemptions";
    private const string LinkOrdinalIndex = "ix_link_ordinal";
    private const string TenantRedeemedIndex = "ix_tenant_redeemed";

    private readonly IIdentityAccessManagementRepository _iamRepository;
    private readonly ILogger<SignupLinkRedemptionRepository> _logger;

    public SignupLinkRedemptionRepository(
        IIdentityAccessManagementRepository iamRepository,
        ILogger<SignupLinkRedemptionRepository> logger)
    {
        _iamRepository = iamRepository;
        _logger = logger;
    }

    private IMongoCollection<SignupLinkRedemption> Collection =>
        _iamRepository.GetCollectionByName<SignupLinkRedemption>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        try
        {
            var keys = Builders<SignupLinkRedemption>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<SignupLinkRedemption>(
                    keys.Ascending(x => x.LinkId).Ascending(x => x.RedemptionOrdinal),
                    new CreateIndexOptions { Name = LinkOrdinalIndex }),
                new CreateIndexModel<SignupLinkRedemption>(
                    keys.Ascending(x => x.TenantId).Descending(x => x.RedeemedAtUtc),
                    new CreateIndexOptions { Name = TenantRedeemedIndex }),
            };
            await Collection.Indexes.CreateManyAsync(models, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignupLinkRedemptionRepository.EnsureIndexesAsync failed; will retry later.");
        }
    }

    public Task InsertAsync(SignupLinkRedemption entity) =>
        Collection.InsertOneAsync(entity);

    public async Task<long> CountRejectedForLinksAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        IEnumerable<string> linkIds)
    {
        var ids = linkIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList()
            ?? new List<string>();
        if (ids.Count == 0)
        {
            return 0;
        }

        var filter = Builders<SignupLinkRedemption>.Filter.Eq(x => x.TenantId, tenantId)
            & Builders<SignupLinkRedemption>.Filter.Eq(x => x.Outcome, SignupLinkRedemptionOutcome.Rejected)
            & Builders<SignupLinkRedemption>.Filter.Gte(x => x.RedeemedAtUtc, fromUtc)
            & Builders<SignupLinkRedemption>.Filter.Lt(x => x.RedeemedAtUtc, toUtc)
            & Builders<SignupLinkRedemption>.Filter.In(x => x.LinkId, ids);

        return await Collection.CountDocumentsAsync(filter);
    }
}
