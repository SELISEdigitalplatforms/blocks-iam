using Iam.DomainService.Entities;
using Iam.DomainService.Activity.RequestModel;
using Blocks.Genesis.Entities.Iam;


namespace Iam.DomainService.Services
{
    /// <summary>What earlier successful logins tell about a new one.</summary>
    public sealed record LoginHistory(bool HasEarlierLogin, bool SeenThisDevice);

    public interface IUserActivityRepository
    {
        Task InsertAsync(UserActivity activity, CancellationToken ct);
        Task<List<UserActivity>> GetAsync(string userId, GetActivitiesRequest req, CancellationToken ct);
        Task<long> CountAsync(string userId, GetActivitiesRequest req, CancellationToken ct);

        /// <summary>
        /// Whether the user has any earlier activity with event <paramref name="loginEvent"/> and
        /// outcome success, and whether one of them came from the same IP address and user agent.
        /// Two existence checks (limit 1); null when the read is not allowed.
        /// </summary>
        Task<LoginHistory?> GetLoginHistoryAsync(string userId, string loginEvent, string? ipAddress, string? userAgent, CancellationToken ct);

        /// <summary>
        /// Counts Rejected signup-link redemptions in [fromUtc, toUtc) whose link id is in
        /// <paramref name="linkIds"/>: the <c>signup-link-redeem</c> events, plus the rows of the old
        /// <c>SignupLinkRedemptions</c> collection while that collection still exists in this database.
        /// No redemption is counted twice, before or after the migration.
        /// </summary>
        Task<long> CountSignupLinkRejectionsAsync(
            string tenantId,
            DateTime fromUtc,
            DateTime toUtc,
            IEnumerable<string> linkIds,
            CancellationToken ct = default);
    }
}