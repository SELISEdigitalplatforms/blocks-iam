namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkConfigurationRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(SignupLinkConfiguration entity);
    Task<bool> ReplaceAsync(SignupLinkConfiguration entity);
    Task<SignupLinkConfiguration?> GetByIdAsync(string itemId, string tenantId);
    Task<SignupLinkConfiguration?> FindByNameAsync(string tenantId, string name, string? excludeItemId = null);
    Task<(List<SignupLinkConfiguration> Items, long TotalCount)> QueryAsync(
        string tenantId,
        QuerySignupLinkConfigurationsRequest request);
}
