using Authentication.DomainService.Security.Models;
using Iam.DomainService.Activity.RequestModel;

namespace Authentication.DomainService.Security.Services
{
    public interface IActivityQueryService
    {
        Task<ActivityPageResponse> GetActivityPageAsync(
            string userId,
            GetActivitiesRequest req,
            CancellationToken ct);

        Task<IReadOnlyList<ActivityItemDto>> GetActivitiesForSessionAsync(
            string userId,
            string sessionId,
            int page,
            int pageSize,
            CancellationToken ct);
    }
}
