namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(SignupLink entity);
    Task<bool> ReplaceAsync(SignupLink entity);
    Task<SignupLink?> GetByIdAsync(string itemId, string tenantId);
    Task<SignupLink?> GetByCodeHashAsync(string codeHash);
    Task<SignupLink?> GetByItemIdAsync(string itemId);
    /// <summary>
    /// Atomically increments RedemptionCount when the link is still redeemable.
    /// Returns the updated document, or null if the race/guard failed.
    /// </summary>
    Task<SignupLink?> TryIncrementRedemptionAsync(
        string linkId,
        string tenantId,
        string createdUserId,
        DateTime nowUtc);
    Task<(List<SignupLink> Items, long TotalCount)> QueryAsync(
        string tenantId,
        QuerySignupLinksRequest request);
    Task<long> RevokeActiveByConfigurationAsync(
        string tenantId,
        string configurationId,
        string revokedBy,
        DateTime revokedAtUtc);
}
