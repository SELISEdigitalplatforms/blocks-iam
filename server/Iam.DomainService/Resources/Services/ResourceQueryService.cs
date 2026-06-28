using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
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

            var (data, count) = await _resourceRepository.GetPermissionsAsync(query);

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

            var (data, count) = await _resourceRepository.GetRolesAsync(query);

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


    }
}
