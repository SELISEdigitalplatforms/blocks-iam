using Iam.DomainService.Entities;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources
{
    public interface IResourceRepository
    {
        Task<Permission> GetPermissionByResourceAsync(string resource);
        Task<Permission> GetPermissionByIdAsync(string id);
        Task<(IQueryable<Permission>, long)> GetPermissionsAsync(GetPermissionsRequest query);
        Task<bool> InsertPermissionAsync(Permission permission);
        Task<bool> UpdatePermissionAsync(Permission permission);
        Task<Role> GetRoleByIdAsync(string id);
        Task<(IQueryable<Role>, long)> GetRolesAsync(GetRolesRequest query);
        Task<Role> GetRoleBySlugAsync(string slug);
        
        /// <summary>
        /// Get role by slug and organization. Enforces org-scoped lookup.
        /// </summary>
        Task<Role> GetRoleBySlugAndOrgAsync(string slug, string organizationId);
        
        /// <summary>
        /// Get all roles in a specific organization.
        /// </summary>
        Task<List<Role>> GetRolesByOrgAsync(string organizationId);
        
        Task<bool> InsertRoleAsync(Role role);
        Task<bool> UpdateRoleAsync(Role role);
        Task<ResourceTimeline<T>> GetResourceTimelineAsync<T>(string itemId);
        Task<bool> SaveResourceTimelineAsync<T>(ResourceTimeline<T> timeline);
        Task<bool> SaveResourceTimelinesAsync<T>(List<ResourceTimeline<T>> timelines);
        Task<bool> UpdateRolePermissionByIdsAsync(string slug, List<string> permissions);
        Task<bool> RemoveRolePermissionByIdsAsync(string slug, List<string> permissions);
        Task<bool> UpdateRolesCountAsync(string slug);
        Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync();
        Task<Organization> GetOrganizationById(string resourceId);
        Task SaveOrganizationAsync(Organization organization);
        Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request);
        Task SaveOrganizationConfig(OrganizationConfig config);
        Task<OrganizationConfig> GetOrganizationConfigAsync(string tenantId, string organizationId);
        Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync();
    }
}
