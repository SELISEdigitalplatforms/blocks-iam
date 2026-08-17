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
        Task<(IQueryable<Permission>, long)> GetPermissionsAsync(GetPermissionsRequest query, string? orgainzationId = null);
        Task<bool> InsertPermissionAsync(Permission permission);
        Task<bool> UpdatePermissionAsync(Permission permission);
        Task<Role> GetRoleByIdAsync(string id);
        Task<(IQueryable<Role>, long)> GetRolesAsync(GetRolesRequest query, string? orgainzationId = null);
        Task<Role> GetRoleBySlugAsync(string slug, string? orgainzationId = null);
        
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
        Task<bool> UpdateRolePermissionByIdsAsync(string slug, List<string> permissions, string? orgainzationId = null);
        Task<bool> RemoveRolePermissionByIdsAsync(string slug, List<string> permissions, string? orgainzationId = null);
        Task<bool> UpdateRolesCountAsync(string slug, string? orgainzationId = null);
        Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync(string? orgainzationId = null);
        Task<Organization> GetOrganizationById(string id);
        Task<Organization> GetOrganizationByNameAsync(string name);
        Task<List<string>> GetOrganizationIdsByUserIdAsync(string userId);
        Task<List<Organization>> GetOrganizationsByIdsAsync(List<string> organizationIds);
        Task SaveOrganizationAsync(Organization organization);
        Task DeleteOrganizationAsync(string organizationId);
        Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request);
        Task SaveOrganizationConfig(TenantConfiguration tenantConfiguration);
        Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync(string? orgainzationId = null);
        Task<TenantConfiguration> GetTenantConfigurationAsync();
        Task<List<Role>> GetRolesBySlugAndOrgAsync(List<string> slugs, string organizationId);
        Task<bool> InsertRolesAsync(List<Role> roles);
        Task<bool> UpdateAllSamePermissionAsync(Permission permission);
        Task<List<Permission>> GetPermissionsByOrgAsync(string organizationId, int? pageNumber = null, int? pageSize = null);
        Task<bool> AddRoleToPermissionsByResourcesAsync(string slug, List<string> resources, string organizationId);
        Task<bool> RemoveRoleFromPermissionsByResourcesAsync(string slug, List<string> resources, string organizationId);

        /// <summary>True when a non-archived role in the organization has this slug as its parent.</summary>
        Task<bool> HasChildRolesAsync(string slug, string organizationId);

        /// <summary>True when an active user in the organization still holds this role.</summary>
        Task<bool> HasUserAssignmentsAsync(string slug, string organizationId);

        /// <summary>Removes the slug from every permission in the organization that references it.</summary>
        Task<bool> RemoveRoleFromAllPermissionsAsync(string slug, string organizationId);

        /// <summary>Removes the slug from the organization's bucket in every user holding it.</summary>
        Task<bool> RemoveRoleFromAllUsersAsync(string slug, string organizationId);
        Task<List<Permission>> GetPermissionsByRoleAsync(string roleSlug, string organizationId);
        Task<List<Permission>> GetPermissionsByRolesAsync(List<string> roleSlugs, string organizationId, int pageNumber = 1, int pageSize = 10);
        Task<List<Permission>> GetPermissionsByGroupsAsync(List<string> groups, string organizationId, int pageNumber = 1, int pageSize = 10);
        Task<List<Permission>> GetPermissionsByIdsAsync(List<string> ids);
        Task<bool> InsertPermissionsAsync(List<Permission> permissions);
        Task<List<Permission>> GetFeResourceFeaturesAsync(List<string> roleSlugs, List<string> permissionKeys, string? search = null, bool? isBuiltIn = null, string? orgainzationId = null);
        Task<List<Permission>> GetPermissionsByResourceAsync(string resource);
        Task<List<Role>> GetRolesBySlugAsync(string slug);
        Task<bool> UpdatePermissionsAsync(List<Permission> permissions);
        Task<bool> UpdateRolesAsync(List<Role> roles);
        Task<List<string>> GetAllOrgIdsAsync();
    }
}
