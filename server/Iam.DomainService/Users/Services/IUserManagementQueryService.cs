using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public interface IUserManagementQueryService
    {
        Task<bool> IsUserAvailableAsync(IsEmailAvailableRequest query);
        Task<GetUsersResponse> GetUsersAsync(GetUsersRequest query);
        Task<GetUserResponse> GetUserAsync(string id);
        Task<GetUserResponse> GetAccountAsync();
        Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request);
    }
}
