namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(SignupLink entity);
    Task<bool> ReplaceAsync(SignupLink entity);
    Task<SignupLink?> GetByIdAsync(string itemId, string tenantId);
    Task<(List<SignupLink> Items, long TotalCount)> QueryAsync(
        string tenantId,
        QuerySignupLinksRequest request);
    Task<long> RevokeActiveByConfigurationAsync(
        string tenantId,
        string configurationId,
        string revokedBy,
        DateTime revokedAtUtc);
}
