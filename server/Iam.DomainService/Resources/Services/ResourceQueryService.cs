using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Resources
{
    public class ResourceQueryService : IResourceQueryService
    {
        private readonly ILogger<ResourceQueryService> _logger;
        private readonly IResourceRepository _resourceRepository;

        public ResourceQueryService(
            ILogger<ResourceQueryService> logger,
            IResourceRepository resourceRepository
        )
        {
            _logger = logger;
            _resourceRepository = resourceRepository;
        }

        public async Task<GetPermissionResponse> GetPermissionAsync(string id)
        {
            _logger.LogInformation("Permission get start");

            var permission = await _resourceRepository.GetPermissionByIdAsync(id);

            _logger.LogInformation("Permission get end");

            return new GetPermissionResponse
            {
                Data = permission
            };
        }

        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync()
        {
            return await _resourceRepository.GetPermissionsGroupBySeverityAsync();
        }

        public async Task<GetPermissionsResponse> GetPermissionsAsync(GetPermissionsRequest query)
        {
            _logger.LogInformation("Permissions get start");
            var bc = BlocksContext.GetContext();
            var orgId = bc?.OrganizationId == "default" && !string.IsNullOrWhiteSpace(query.OrganizationId)
                ? query.OrganizationId
                : null;

            var (data, count) = await _resourceRepository.GetPermissionsAsync(query, orgId);

            _logger.LogInformation("Permissions get end");

            return new GetPermissionsResponse
            {
                Data = data,
                TotalCount = count
            };
        }

        public async Task<GetRoleResponse> GetRoleAsync(string id)
        {
            _logger.LogInformation("Role get start");

            var role = await _resourceRepository.GetRoleByIdAsync(id);

            _logger.LogInformation("Role get end");

            return new GetRoleResponse
            {
                Data = role
            };
        }

        public async Task<GetRolesResponse> GetRolesAsync(GetRolesRequest query)
        {
            _logger.LogInformation("Roles get start");
            var bc = BlocksContext.GetContext();
            var orgId = bc?.OrganizationId == "default" && !string.IsNullOrWhiteSpace(query.OrganizationId)
                ? query.OrganizationId
                : null;

            var (data, count) = await _resourceRepository.GetRolesAsync(query, orgId);

            _logger.LogInformation("Roles get end");

            return new GetRolesResponse
            {
                Data = data,
                TotalCount = count
            };
        }

        public async Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync()
        {
            return await _resourceRepository.GetResourceGroupsAsync();
        }

        public async Task<List<GetFeResourceFeatureResponse>> GetFeResourceFeaturesAsync(GetFeResourceFeatureRequest request)
        {
            var bc = BlocksContext.GetContext();
            var roles = bc?.Roles ?? [];
            var permissions = bc?.Permissions ?? [];

            if (!roles.Any() && !permissions.Any())
            {
                return [];
            }

            var data = await _resourceRepository.GetFeResourceFeaturesAsync(
                roles.ToList(),
                permissions.ToList(),
                request?.Search,
                request?.IsBuiltIn
            );

            return data
                .Where(x => x.Type == ResourceType.FrontendAction)
                .Select(x => new GetFeResourceFeatureResponse
                {
                    Resource = x.Resource,
                    Name = x.Name,
                    Description = x.Description
                })
                .ToList();
        }

        public async Task<GetAssignableRolesResponse> GetAssignableRolesAsync()
        {
            var bc = BlocksContext.GetContext();
            var userRoles = bc?.Roles ?? [];

            var (roles, count) = await _resourceRepository.GetRolesAsync(new GetRolesRequest
            {
                PageSize = 1000
            });

            var referencedAncestorSlugs = roles
                .SelectMany(x => x.AncestorRoleSlugs ?? new List<string>())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // User roles that can create descendants
            var creatableRoles = roles
                .Where(x =>
                    userRoles.Contains(x.Slug, StringComparer.OrdinalIgnoreCase)
                    && x.CanCreateOwn)
                .Select(x => x.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hierarchy = new List<AssignableRole>();
            var standalone = new List<AssignableRole>();

            foreach (var role in roles)
            {
                var isStandalone =
                    !role.CanCreateOwn &&
                    string.IsNullOrWhiteSpace(role.ParentRoleSlug) &&
                    !role.AncestorRoleSlugs.Any() &&
                    !referencedAncestorSlugs.Contains(role.Slug);

                if (isStandalone)
                {
                    standalone.Add(new AssignableRole
                    {
                        Slug = role.Slug,
                        Name = role.Name
                    });

                    continue;
                }

                var isDescendantOrSelf =
                    creatableRoles.Contains(role.Slug) ||
                    role.AncestorRoleSlugs.Any(a =>
                        creatableRoles.Contains(a));

                if (isDescendantOrSelf)
                {
                    hierarchy.Add(new AssignableRole
                    {
                        Slug = role.Slug,
                        Name = role.Name
                    });
                }
            }

            return new GetAssignableRolesResponse
            {
                Hierarchy = hierarchy,
                Standalone = standalone
            };
        }

        public async Task<RoleArchiveImpactResponse> GetRoleArchiveImpactAsync(string id)
        {
            _logger.LogInformation("Role archive impact start");

            var role = await _resourceRepository.GetRoleByIdAsync(id);
            if (role == null)
            {
                _logger.LogInformation("Role archive impact end -- Not Found");
                return new RoleArchiveImpactResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "ItemId", "Role_Not_Found" } }
                };
            }

            var isMultiOrgEnabled = MultiOrgMode.IsEnabled(
                await _resourceRepository.GetTenantConfigurationAsync(), _logger);

            // Only copies the archive would actually reach: already-archived ones are skipped by the
            // propagation, so counting them here would overstate the blast radius and make the
            // preview disagree with what happens next.
            var otherOrgIds = isMultiOrgEnabled
                ? (await _resourceRepository.GetNonArchivedRolesBySlugAsync(role.Slug))
                    .Where(x => x.ItemId != role.ItemId
                        && !string.Equals(x.OrganizationId, role.OrganizationId, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.OrganizationId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            // The target's own organization is always counted for users, never for organizations:
            // "2 other organizations" and "3 users" are answers to different questions.
            var userScopeOrgIds = otherOrgIds.Append(role.OrganizationId).ToList();

            var hasChildRoles = await _resourceRepository.HasChildRolesAsync(role.Slug, role.OrganizationId);

            _logger.LogInformation("Role archive impact end");

            return new RoleArchiveImpactResponse
            {
                IsSuccess = true,
                Slug = role.Slug,
                Name = role.Name,
                IsMultiOrgEnabled = isMultiOrgEnabled,
                OrganizationCount = otherOrgIds.Count,
                AffectedUserCount = (int)await _resourceRepository.CountUsersWithRoleAsync(role.Slug, userScopeOrgIds, activeOnly: false),
                ActiveUserCount = (int)await _resourceRepository.CountUsersWithRoleAsync(role.Slug, userScopeOrgIds, activeOnly: true),
                Blocked = hasChildRoles,
                BlockingReason = hasChildRoles ? "Role_Has_Child_Roles" : null
            };
        }

        public async Task<PermissionArchiveImpactResponse> GetPermissionArchiveImpactAsync(string id)
        {
            _logger.LogInformation("Permission archive impact start");

            var permission = await _resourceRepository.GetPermissionByIdAsync(id);
            if (permission == null)
            {
                _logger.LogInformation("Permission archive impact end -- Not Found");
                return new PermissionArchiveImpactResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "ItemId", "Permission_Not_Found" } }
                };
            }

            var isMultiOrgEnabled = MultiOrgMode.IsEnabled(
                await _resourceRepository.GetTenantConfigurationAsync(), _logger);

            // Unlike Role, IsArchived has always existed on Permission and is present on every
            // document, so filtering it in memory here is safe.
            var otherOrgIds = isMultiOrgEnabled
                ? (await _resourceRepository.GetPermissionsByResourceAsync(permission.Resource))
                    .Where(x => !x.IsArchived
                        && x.ItemId != permission.ItemId
                        && !string.Equals(x.OrganizationId, permission.OrganizationId, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.OrganizationId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            var scopeOrgIds = otherOrgIds.Append(permission.OrganizationId).ToList();

            _logger.LogInformation("Permission archive impact end");

            return new PermissionArchiveImpactResponse
            {
                IsSuccess = true,
                Resource = permission.Resource,
                Name = permission.Name,
                IsMultiOrgEnabled = isMultiOrgEnabled,
                OrganizationCount = otherOrgIds.Count,
                // The direct User.Permissions grant -- the one that actually mints a permission
                // claim -- and the role bindings are separate populations and are reported apart.
                AffectedUserCount = (int)await _resourceRepository.CountUsersWithPermissionAsync(permission.Resource, scopeOrgIds),
                RoleBindingCount = (int)await _resourceRepository.CountRoleBindingsForResourceAsync(permission.Resource, scopeOrgIds),
                // Permissions have no dependency that can hard-block an archive. Kept on the
                // envelope so both dialogs render from one shape.
                Blocked = false,
                BlockingReason = null
            };
        }
    }
}
