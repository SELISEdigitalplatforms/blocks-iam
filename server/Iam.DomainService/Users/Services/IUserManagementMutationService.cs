using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Users
{
    public interface IUserManagementMutationService
    {
        Task<BaseMutationResponse> CreateUserAsync(CreateUserRequest command);
        Task<BaseMutationResponse> UpdateUserAsync(UpdateUserRequest command);
        Task UpdateUserByLoginInfoAsync(RefreshTokenEvent refreshTokenConsumer);
        Task ExecuteUserMutationCommandAsync(UserMutationEvent command);
        Task<bool> CreateUserByEmailAsync(CreateUserByEmailEvent @event);
        Task<BaseMutationResponse> CreateUserFromSsoAsync(CreateUserViaSsoRequest command);
        Task ExecuteUserMutationViaSsoCommandAsync(CreateUserViaSsoEvent command);
        Task<bool> ProcessCreateUserByEmailAfterActionAsync(CreateUserByEmailEvent @event, string userId);
        Task<BaseResponse> DeactivateUserAsync(DeactivateUserRequest request);
        Task<BaseMutationResponse> UpdateUserAccessControlAsync(UpdateUserAccessControlRequest command);
        Task<BaseMutationResponse> RevokeUserAccessControlAsync(RevokeUserAccessControlRequest command);
        Task<TenantConfiguration> GetTenantConfigurationAsync();
    }
}
