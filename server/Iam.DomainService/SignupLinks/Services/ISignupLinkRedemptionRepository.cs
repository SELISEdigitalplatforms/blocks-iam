namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkRedemptionRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(SignupLinkRedemption entity);
    /// <summary>
    /// Count Rejected redemptions in [fromUtc, toUtc) whose LinkId is in <paramref name="linkIds"/>.
    /// </summary>
    Task<long> CountRejectedForLinksAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        IEnumerable<string> linkIds);
}
