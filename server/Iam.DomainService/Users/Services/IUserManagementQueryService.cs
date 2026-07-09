using Iam.DomainService.Entities;

namespace Iam.DomainService.Users
{
    public interface IUserManagementQueryService
    {
        Task<bool> IsUserAvailableAsync(IsEmailAvailableRequest query);
        Task<IsUserExistResponse> IsUserExistAsync(string email);
        Task<GetUsersResponse> GetUsersAsync(GetUsersRequest query);
        Task<GetUserResponse> GetUserAsync(string id, string? organizationId);
        Task<GetUserResponse> GetAccountAsync();
        Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request);
    }
}
