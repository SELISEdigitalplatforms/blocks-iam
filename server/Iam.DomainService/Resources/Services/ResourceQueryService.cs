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
        private const string DefaultOrganizationId = "default";

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

            // One read, two answers: the configuration document carries both the multi-org flag and
            // the signup defaults, so the signup check below costs no extra query.
            var tenantConfiguration = await _resourceRepository.GetTenantConfigurationAsync();
            var isMultiOrgEnabled = MultiOrgMode.IsEnabled(tenantConfiguration, _logger);

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
                IsSignUpDefault = tenantConfiguration?.DefaultRolesForNewUserOnSignUp?
                    .Any(x => string.Equals(x, role.Slug, StringComparison.OrdinalIgnoreCase)) ?? false,
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

            // One read, two answers -- see GetRoleArchiveImpactAsync.
            var tenantConfiguration = await _resourceRepository.GetTenantConfigurationAsync();
            var isMultiOrgEnabled = MultiOrgMode.IsEnabled(tenantConfiguration, _logger);

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
                IsSignUpDefault = tenantConfiguration?.DefaultPermissionsForNewUserOnSignUp?
                    .Any(x => string.Equals(x, permission.Resource, StringComparison.OrdinalIgnoreCase)) ?? false,
                // Permissions have no dependency that can hard-block an archive. Kept on the
                // envelope so both dialogs render from one shape.
                Blocked = false,
                BlockingReason = null
            };
        }

        public async Task<RolePermissionChangeImpactResponse> GetRolePermissionChangeImpactAsync(
            RolePermissionChangeImpactRequest request)
        {
            _logger.LogInformation("Role permission change impact start");

            if (request == null || string.IsNullOrWhiteSpace(request.Slug))
            {
                _logger.LogInformation("Role permission change impact end -- Validation Error");
                return new RolePermissionChangeImpactResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Slug", "Slug_Never_Empty" } }
                };
            }

            // Deliberately the same resolution SetRolesAsync uses, not the stricter default-org-only
            // idiom the list queries use. A preview scoped to a different organization than the
            // write would describe a change that never happens.
            var organizationId = ResolveOrganizationId(request.OrganizationId);

            var role = await _resourceRepository.GetRoleBySlugAsync(request.Slug, organizationId);
            if (role == null)
            {
                _logger.LogInformation("Role permission change impact end -- Not Found");
                return new RolePermissionChangeImpactResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Role", "Role_Not_Found" } }
                };
            }

            // SetRolesAsync refuses an archived role, so the preview has to refuse it too --
            // otherwise the dialog offers a confirmation the save is guaranteed to reject.
            if (role.IsArchived)
            {
                _logger.LogInformation("Role permission change impact end -- Archived");
                return new RolePermissionChangeImpactResponse
                {
                    IsSuccess = false,
                    Slug = role.Slug,
                    Name = role.Name,
                    Errors = new Dictionary<string, string> { { "archived", "Role_Already_Archived" } }
                };
            }

            var isMultiOrgEnabled = MultiOrgMode.IsEnabled(
                await _resourceRepository.GetTenantConfigurationAsync(), _logger);

            // Both halves of the gate ProcessPermissionAsync enforces. Offering the option on the
            // strength of multi-org alone would show an organization-scoped administrator a control
            // the backend then ignores.
            var canPropagate = isMultiOrgEnabled && organizationId == DefaultOrganizationId;

            var addPermissions = request.AddPermissions.Any()
                ? await _resourceRepository.GetPermissionsByIdsAsync(request.AddPermissions) ?? []
                : [];

            var removePermissions = request.RemovePermissions.Any()
                ? await _resourceRepository.GetPermissionsByIdsAsync(request.RemovePermissions) ?? []
                : [];

            // Only the copies propagation would actually reach. Archived copies are skipped by
            // PropagateSetPermissionsAsync, so counting them would overstate the blast radius and
            // make the preview disagree with what happens next -- the same reasoning as the archive
            // impact previews.
            var otherOrgIds = canPropagate
                ? (await _resourceRepository.GetNonArchivedRolesBySlugAsync(role.Slug))
                    .Where(x => x.ItemId != role.ItemId
                        && !string.Equals(x.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.OrganizationId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            // Organizations that exist but hold no live copy of this role: propagation logs a
            // warning and moves on for each of them. Reported so the administrator learns about the
            // drift here rather than from a log nobody reads. It counts only the missing-or-archived
            // role case; an organization that has the role but never received a copy of one of the
            // permissions is resolved per-organization at write time and is not previewed, because
            // that would cost one query per organization per resource.
            var skippedOrganizationCount = 0;
            if (canPropagate)
            {
                var reachedOrgIds = otherOrgIds
                    .Append(organizationId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                skippedOrganizationCount = (await _resourceRepository.GetAllOrgIdsAsync())
                    .Count(x => !reachedOrgIds.Contains(x));
            }

            // The role's own organization is always counted for users, never for organizations:
            // "2 other organizations" and "3 users" answer different questions.
            var userScopeOrgIds = otherOrgIds.Append(organizationId).ToList();

            _logger.LogInformation("Role permission change impact end");

            return new RolePermissionChangeImpactResponse
            {
                IsSuccess = true,
                Slug = role.Slug,
                Name = role.Name,
                IsMultiOrgEnabled = isMultiOrgEnabled,
                CanPropagate = canPropagate,
                // Resolved documents, not requested ids: an id with no document behind it binds
                // nothing, and promising it in the preview would be a lie the save cannot keep.
                AddCount = addPermissions.Count,
                RemoveCount = removePermissions.Count,
                OrganizationCount = otherOrgIds.Count,
                SkippedOrganizationCount = skippedOrganizationCount,
                AffectedUserCount = (int)await _resourceRepository.CountUsersWithRoleAsync(role.Slug, userScopeOrgIds, activeOnly: false),
                ActiveUserCount = (int)await _resourceRepository.CountUsersWithRoleAsync(role.Slug, userScopeOrgIds, activeOnly: true)
            };
        }

        /// <summary>
        /// Mirrors the mutation side's resolution so a preview and the write it previews always
        /// target the same organization.
        /// </summary>
        private static string ResolveOrganizationId(string? organizationId)
        {
            if (!string.IsNullOrWhiteSpace(organizationId))
            {
                return organizationId;
            }

            var contextOrgId = BlocksContext.GetContext()?.OrganizationId;
            return string.IsNullOrWhiteSpace(contextOrgId) ? DefaultOrganizationId : contextOrgId;
        }
    }
}
