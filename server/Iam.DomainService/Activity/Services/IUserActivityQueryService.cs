using Blocks.Genesis;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Activity.Services
{
    public interface IUserActivityQueryService
    {
        Task<BaseQueryListResponse<IQueryable<UserActivity>>> GetActivitiesAsync(
            string? requestedUserId,
            GetActivitiesRequest req,
            CancellationToken ct);
    }
}