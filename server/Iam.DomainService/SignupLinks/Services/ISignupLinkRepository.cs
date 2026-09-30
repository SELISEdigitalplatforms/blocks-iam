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
    /// <param name="scope">Narrows to one organization and, optionally, one creator. Null reaches the whole tenant.</param>
    Task<(List<SignupLink> Items, long TotalCount)> QueryAsync(
        string tenantId,
        QuerySignupLinksRequest request,
        SignupLinkScope? scope);
    Task<long> RevokeActiveByConfigurationAsync(
        string tenantId,
        string configurationId,
        string revokedBy,
        DateTime revokedAtUtc,
        SignupLinkScope? scope);
    /// <summary>
    /// Links for one configuration whose CreatedDate is in [fromUtc, toUtc).
    /// </summary>
    Task<List<SignupLink>> FindForSummaryAsync(
        string tenantId,
        string configurationId,
        DateTime fromUtc,
        DateTime toUtc);
}

/// <summary>Which links a caller may list and revoke: those of its own organization.</summary>
public sealed record SignupLinkScope(string OrganizationId)
{
    public bool Allows(SignupLink link) =>
        string.Equals(link.OrganizationId, OrganizationId, StringComparison.Ordinal);
}
