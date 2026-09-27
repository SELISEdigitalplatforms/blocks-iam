using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users.RequestModel;

namespace Iam.DomainService.Users
{
    public interface IUserManagementMutationService
    {
        Task<BaseMutationResponse> CreateUserAsync(CreateUserRequest command);
        Task<BaseMutationResponse> UpdateUserAsync(UpdateUserRequest command);

        Task<BaseMutationResponse> UpdateMyAccountAsync(UpdateMyAccountRequest command);
        Task ExecuteUserMutationCommandAsync(UserMutationEvent command);
        Task<bool> CreateUserByEmailAsync(CreateUserByEmailEvent @event);
        Task<BaseMutationResponse> CreateUserFromSsoAsync(CreateUserViaSsoRequest command);
        Task ExecuteUserMutationViaSsoCommandAsync(CreateUserViaSsoEvent command);
        Task<bool> ProcessCreateUserByEmailAfterActionAsync(CreateUserByEmailEvent @event, string userId);
        Task<BaseResponse> DeactivateUserAsync(DeactivateUserRequest request);
        Task<BaseMutationResponse> ActivateUserAsync(ActivateUserByAdminRequest request);
        Task<BaseMutationResponse> ActivateAndLinkSocialIdentityAsync(ActivateAndLinkSocialIdentityRequest request);
        Task<BaseMutationResponse> UpdateUserAccessControlAsync(UpdateUserAccessControlRequest command);
        Task<BaseMutationResponse> RevokeUserAccessControlAsync(RevokeUserAccessControlRequest command);

        /// <summary>
        /// Dry-run a bulk role delta: resolve the target and report how many users would change,
        /// writing nothing. The only safety gate before <see cref="SubmitBulkRoleChangeAsync"/>.
        /// </summary>
        Task<BulkRoleChangePreviewResponse> PreviewBulkRoleChangeAsync(BulkRoleChangeRequest command);

        /// <summary>
        /// Validate, resolve and queue a bulk role delta. Returns as soon as the work is on the
        /// queue -- nothing is written in the request path.
        /// </summary>
        Task<BulkRoleChangeSubmitResponse> SubmitBulkRoleChangeAsync(BulkRoleChangeRequest command);

        /// <summary>
        /// Apply one queued chunk. Called by the worker consumer only; per-user failures are logged
        /// and swallowed so one bad id cannot cause the whole chunk to be redelivered.
        /// </summary>
        Task ApplyBulkRoleChangeAsync(BulkUserRoleChangeEvent command);
        Task<TenantConfiguration> GetTenantConfigurationAsync();
    }
}
