using Iam.DomainService.Entities;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources
{
    public interface IResourceRepository
    {
        Task<Permission> GetPermissionByResourceAsync(string resource, string? organizationId = "default");
        Task<List<Permission>> GetPermissionsByResourcesAsync(List<string> resources, string? organizationId = "default");
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
        Task<Organization> GetOrganizationById(string id);
        Task<List<string>> GetOrganizationIdsByUserIdAsync(string userId);
        Task<List<Organization>> GetOrganizationsByIdsAsync(List<string> organizationIds);
        Task SaveOrganizationAsync(Organization organization);
        Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request);
        Task SaveOrganizationConfig(TenantConfiguration tenantConfiguration);
        Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync();
        Task<TenantConfiguration> GetTenantConfigurationAsync();
        Task<List<Role>> GetRolesBySlugAndOrgAsync(List<string> slugs, string organizationId);
        Task<bool> InsertRolesAsync(List<Role> roles);
        Task<bool> UpdateAllSamePermissionAsync(Permission permission);
        Task<List<Permission>> GetPermissionsByOrgAsync(string organizationId, int? pageNumber = null, int? pageSize = null);
        Task<bool> AddRoleToPermissionsByResourcesAsync(string slug, List<string> resources, string organizationId);
        Task<bool> RemoveRoleFromPermissionsByResourcesAsync(string slug, List<string> resources, string organizationId);
        Task<List<Permission>> GetPermissionsByRoleAsync(string roleSlug, string organizationId);
        Task<List<Permission>> GetPermissionsByRolesAsync(List<string> roleSlugs, string organizationId, int pageNumber = 1, int pageSize = 10);
        Task<List<Permission>> GetPermissionsByGroupsAsync(List<string> groups, string organizationId, int pageNumber = 1, int pageSize = 10);
        Task<List<Permission>> GetPermissionsByIdsAsync(List<string> ids);
        Task<bool> InsertPermissionsAsync(List<Permission> permissions);
        Task<List<Permission>> GetFeResourceFeaturesAsync(List<string> roleSlugs, List<string> permissionKeys, string? search = null, bool? isBuiltIn = null);
        Task<List<Permission>> GetPermissionsByResourceAsync(string resource);
        Task<List<Role>> GetRolesBySlugAsync(string slug);
        Task<bool> UpdatePermissionsAsync(List<Permission> permissions);
        Task<bool> UpdateRolesAsync(List<Role> roles);
    }
}
