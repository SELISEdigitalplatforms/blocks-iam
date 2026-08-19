using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.Resources
{
    public interface IResourceQueryService
    {
        Task<GetPermissionsResponse> GetPermissionsAsync(GetPermissionsRequest query);
        Task<GetPermissionResponse> GetPermissionAsync(string id);
        Task<GetRolesResponse> GetRolesAsync(GetRolesRequest query);
        Task<GetRoleResponse> GetRoleAsync(string id);
        Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync();
        Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync();
        Task<List<GetFeResourceFeatureResponse>> GetFeResourceFeaturesAsync(GetFeResourceFeatureRequest request);
        Task<GetAssignableRolesResponse> GetAssignableRolesAsync();

        /// <summary>What archiving this role would affect, across every organization.</summary>
        Task<RoleArchiveImpactResponse> GetRoleArchiveImpactAsync(string id);

        /// <summary>What archiving this permission would affect, across every organization.</summary>
        Task<PermissionArchiveImpactResponse> GetPermissionArchiveImpactAsync(string id);
    }
}
