using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.Resources
{
    public interface IResourceMutationService
    {
        Task<BaseMutationResponse> CreatePermissionAsync(CreatePermissionRequest command);
        Task<BaseMutationResponse> UpdatePermissionAsync(string id, UpdatePermissionRequest command);
        Task<BaseMutationResponse> CreateRoleAsync(CreateRoleRequest command);
        Task<BaseMutationResponse> UpdateRoleAsync(UpdateRoleRequest command);
        Task<SetRolesResponse> SetRolesAsync(SetRolesRequest command);
        Task ExecuteResourceMutationCommandAsync(ResourceMutationEvent command);
        Task<bool> ProcessPermissionAsync(ResourceSetToPermissionMutationEvent command);
        Task<BaseMutationResponse> CreateOrganizationAsync(CreateOrganizationRequest request, string? creatorId = null);
        Task<BaseResponse> UpdateOrganizationAsync(string id, SaveOrganizationRequest request);
        Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request);
        Task<GetOrganizationResponse> GetOrganizationAsync(string id);
        Task<GetMyOrganizationsResponse> GetMyOrganizationAsync();
        Task<BaseResponse> SaveOrganizationConfigAsync(SaveOrganizationConfigRequest request);
        Task<Dictionary<string, object>> GetOrganizationConfigAsync();
        Task ExecuteOrganizationProvisioningAsync(OrganizationProvisioningEvent command);
        Task ExecutePropagationRolePermissionUpdateAsync(PropagationRolePermissionUpdateEvent command);
        Task ExecutePermissionMutationForTenantsAsync(PermissionMutationForTenantsEvent context);
    }
}
