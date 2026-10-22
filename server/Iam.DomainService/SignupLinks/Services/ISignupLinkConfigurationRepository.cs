using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkConfigurationRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(SignupLinkConfiguration entity);
    Task<bool> ReplaceAsync(SignupLinkConfiguration entity);
    Task<SignupLinkConfiguration?> GetByIdAsync(string itemId, string tenantId);
    /// <summary>As the caller may see it: properties a View column rule hides are cleared. Never save it back.</summary>
    Task<SignupLinkConfiguration?> GetByIdForViewAsync(string itemId, string tenantId);
    Task<SignupLinkConfiguration?> FindByNameAsync(string tenantId, string name, string? excludeItemId = null);
    Task<(List<SignupLinkConfiguration> Items, long TotalCount)> QueryAsync(
        string tenantId,
        QuerySignupLinkConfigurationsRequest request);
}
