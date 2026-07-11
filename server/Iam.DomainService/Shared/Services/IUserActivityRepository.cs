using Iam.DomainService.Entities;
using Iam.DomainService.Activity.RequestModel;


namespace Iam.DomainService.Services
{
    public interface IUserActivityRepository
    {
        Task InsertAsync(UserActivity activity, CancellationToken ct);
        Task<List<UserActivity>> GetAsync(string userId, GetActivitiesRequest req, CancellationToken ct);
        Task<long> CountAsync(string userId, GetActivitiesRequest req, CancellationToken ct);
    }
}