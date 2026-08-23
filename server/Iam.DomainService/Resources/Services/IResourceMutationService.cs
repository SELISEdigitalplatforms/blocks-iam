using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.Resources
{
    public interface IResourceMutationService
    {
        Task<BaseMutationResponse> CreatePermissionAsync(CreatePermissionRequest command);
        Task<BaseMutationResponse> UpdatePermissionAsync(string id, UpdatePermissionRequest command);

        /// <summary>
        /// Archives a permission. Soft delete only — the document is never removed, so audit
        /// history survives. Only a default-organization caller may archive, only a default
        /// -organization record may be archived, and built-in permissions additionally require
        /// root-tenant access.
        /// </summary>
        Task<BaseMutationResponse> ArchivePermissionAsync(string id, bool confirmRevokeFromUsers = false);

        /// <summary>
        /// Archives a role. Soft delete only. Blocks rather than guesses when the role is unsafe to
        /// retire: a role with child roles or active user assignments is refused outright, and a
        /// copy created from the default organization can only be archived via propagation from its
        /// master record.
        /// </summary>
        Task<BaseMutationResponse> ArchiveRoleAsync(string id, bool confirmRevokeFromUsers = false);
        Task<BaseMutationResponse> CreateRoleAsync(CreateRoleRequest command);
        Task<BaseMutationResponse> UpdateRoleAsync(UpdateRoleRequest command);
        Task<SetRolesResponse> SetRolesAsync(SetRolesRequest command);
        Task ExecuteResourceMutationCommandAsync(ResourceMutationEvent command);
        Task<bool> ProcessPermissionAsync(ResourceSetToPermissionMutationEvent command);
        Task<BaseMutationResponse> CreateOrganizationAsync(CreateOrganizationRequest request, string? creatorId = null);
        Task<BaseResponse> UpdateOrganizationAsync(string id, SaveOrganizationRequest request);

        /// <summary>
        /// Removes an organization outright. Used to compensate a failed signup so a
        /// half-created org does not permanently reserve the name the user chose.
        /// </summary>
        Task DeleteOrganizationAsync(string organizationId);
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
