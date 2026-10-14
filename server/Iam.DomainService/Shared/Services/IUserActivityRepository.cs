using Iam.DomainService.Entities;
using Iam.DomainService.Activity.RequestModel;
using Blocks.Genesis.Entities.Iam;


namespace Iam.DomainService.Services
{
    public interface IUserActivityRepository
    {
        Task InsertAsync(UserActivity activity, CancellationToken ct);
        Task<List<UserActivity>> GetAsync(string userId, GetActivitiesRequest req, CancellationToken ct);
        Task<long> CountAsync(string userId, GetActivitiesRequest req, CancellationToken ct);

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