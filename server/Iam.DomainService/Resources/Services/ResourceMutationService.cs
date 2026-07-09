using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Resources
{
    public class ResourceMutationService : IResourceMutationService
    {
        private const string DefaultOrganizationId = "default";
        private readonly ILogger<ResourceMutationService> _logger;
        private readonly IResourceRepository _resourceRepository;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly IValidator<CreatePermissionRequest> _permissionValidator;
        private readonly IValidator<UpdatePermissionRequest> _updatepPermissionValidator;
        private readonly IValidator<CreateRoleRequest> _roleValidator;

        public ResourceMutationService(
            ILogger<ResourceMutationService> logger,
            IResourceRepository resourceRepository,
            IIdentityAccessManagementService identityAccessManagementService,
            IValidator<CreatePermissionRequest> permissionValidator,
            IValidator<UpdatePermissionRequest> updatepPermissionValidator,
            IValidator<CreateRoleRequest> roleValidator
        )
        {
            _logger = logger;
            _resourceRepository = resourceRepository;
            _identityAccessManagementService = identityAccessManagementService;
            _permissionValidator = permissionValidator;
            _updatepPermissionValidator = updatepPermissionValidator;
            _roleValidator = roleValidator;
        }

        public async Task<BaseMutationResponse> CreatePermissionAsync(CreatePermissionRequest command)
        {
            var blocksContext = BlocksContext.GetContext();
            if (!(string.IsNullOrWhiteSpace(blocksContext.OrganizationId) || blocksContext.OrganizationId == DefaultOrganizationId))
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "forbidden", "Not allowed to create permission in this organization" }
                    }
                };
            }

            _logger.LogInformation("Permission creation start");
            var validationResult = await _permissionValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("Permission creation end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }


            var itemId = await ProcessCreatePermissionRequestAsync(command);

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Create,
                    ItemId = itemId,
                    Entity = ResourceEntity.Permission
                }
            );

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (tenantConfig.IsMultiOrgEnabled && command.PropagateToOtherOrg)
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamOrgQueue,
                    new PropagationRolePermissionUpdateEvent
                    {
                        Entity = "permission",
                        ItemId = itemId,
                        Action = "insert"
                    }
                );
            }

            _logger.LogInformation("Permission creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        public async Task<string> ProcessCreatePermissionRequestAsync(CreatePermissionRequest command)
        {
            var blocksContext = BlocksContext.GetContext();

            var permission = new Permission
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                Name = command.Name,
                Description = command.Description,
                Type = command.Type,
                Resource = command.Resource.ToLower(),
                Tags = command.Tags,
                IsBuiltIn = command.IsBuiltIn,
                ResourceGroup = command.ResourceGroup,
                PermissionSeverity = command.PermissionSeverity,
                DependentPermissions = command.DependentPermissions,
                OrganizationId = "default"

            };
            await _resourceRepository.InsertPermissionAsync(permission);

            return permission.ItemId;

        }

        public async Task<BaseMutationResponse> CreateRoleAsync(CreateRoleRequest command)
        {
            _logger.LogInformation("Role creation start");

            var validationResult = await _roleValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var itemId = await ProcessRoleAsync(command);

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Create,
                    ItemId = itemId,
                    Entity = ResourceEntity.Role
                }
            );

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            var blocksContext = BlocksContext.GetContext();

            if (blocksContext.OrganizationId == DefaultOrganizationId && (tenantConfig?.IsMultiOrgEnabled ?? false) & command.PropagateToOtherOrg)
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamOrgQueue,
                    new PropagationRolePermissionUpdateEvent
                    {
                        Entity = "role",
                        ItemId = itemId,
                        Action = "insert"
                    }
                );
            }

            _logger.LogInformation("Role creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        public async Task<string> ProcessRoleAsync(CreateRoleRequest command)
        {
            var blocksContext = BlocksContext.GetContext();
            var tenantId = blocksContext?.TenantId;
            var organizationId = ResolveOrganizationId(blocksContext?.OrganizationId);

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("TenantId is required to create a role");
            }

            var role = new Role
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                OrganizationId = organizationId,  // Role is org-scoped
                Name = command.Name,
                Description = command.Description,
                Slug = command.Slug.ToLower(),
                ParentRoleSlug = string.IsNullOrWhiteSpace(command.ParentRoleSlug)
                    ? null
                    : command.ParentRoleSlug.ToLower(),
                CanCreateOwn = command.CanCreateOwn
            };

            if (!string.IsNullOrWhiteSpace(command.ParentRoleSlug))
            {
                var ancestorRoleSlugs = new List<string>() { command.ParentRoleSlug.ToLower() };
                while (true)
                {
                    var roleParent = await _resourceRepository.GetRoleBySlugAsync(command.ParentRoleSlug.ToLower());
                    if (string.IsNullOrWhiteSpace(roleParent.ParentRoleSlug))
                    {
                        break;
                    }
                    else
                    {
                        ancestorRoleSlugs.Add(roleParent.ParentRoleSlug);
                    }
                }
                role.AncestorRoleSlugs = ancestorRoleSlugs;
            }

            await _resourceRepository.InsertRoleAsync(role);

            return role.ItemId;

        }

        public async Task<BaseMutationResponse> UpdatePermissionAsync(string id, UpdatePermissionRequest command)
        {
            var blocksContext = BlocksContext.GetContext();
            if (!(string.IsNullOrWhiteSpace(blocksContext.OrganizationId) || blocksContext.OrganizationId == DefaultOrganizationId))
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "forbidden", "Not allowed to update permission in this organization" }
                    }
                };
            }

            _logger.LogInformation("Permission update start");

            var permission = await _resourceRepository.GetPermissionByIdAsync(id);
            if (permission == null)
            {
                _logger.LogInformation("Permission update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Permission_Not_Found" }
                    }
                };
            }

            var validationResult = await _updatepPermissionValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("Permission update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            permission.Name = command.Name;
            permission.Description = command.Description;
            permission.Type = command.Type;
            permission.Resource = command.Resource.ToLower();
            permission.LastUpdatedDate = DateTime.Now;
            permission.LastUpdatedBy = blocksContext?.UserId;
            permission.Tags = command.Tags;
            permission.IsArchived = command.IsArchived;
            permission.IsBuiltIn = command.IsBuiltIn;
            permission.ResourceGroup = command.ResourceGroup;
            permission.DependentPermissions = command.DependentPermissions;
            permission.PermissionSeverity = command.PermissionSeverity;

            var result = await _resourceRepository.UpdatePermissionAsync(permission);

            if (!result)
            {
                _logger.LogInformation("Permission update end -- Error");
                return new BaseMutationResponse();
            }

            await _resourceRepository.UpdateAllSamePermissionAsync(permission);

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Update,
                    ItemId = permission.ItemId,
                    Entity = ResourceEntity.Permission
                }
            );

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (tenantConfig.IsMultiOrgEnabled & command.PropagateToOtherOrg)
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamOrgQueue,
                    new PropagationRolePermissionUpdateEvent
                    {
                        Entity = "permission",
                        ItemId = permission.ItemId,
                        Action = "update"
                    }
                );
            }

            _logger.LogInformation("Permission update end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = permission.ItemId
            };
        }

        public async Task<BaseMutationResponse> UpdateRoleAsync(UpdateRoleRequest command)
        {
            _logger.LogInformation("Role update start");
            var role = await _resourceRepository.GetRoleByIdAsync(command.ItemId);
            if (role == null)
            {
                _logger.LogInformation("Role update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Item_Not_Found" }
                    }
                };
            }

            if (role.CreatedFromDefault)
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                        {
                            { "forbidden", "Can_Not_Change_Default_role" }
                        }
                };
            }

            if (string.IsNullOrWhiteSpace(command.Name))
            {
                _logger.LogInformation("Role update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Name", "Should_Not_Be_Empty_Null" }
                    }
                };
            }

            if (command.Name.Count() > 150)
            {
                _logger.LogInformation("Role update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Name", "Maximum_Character_Limit_100" }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();
            var normalizedParentRoleSlug = string.IsNullOrWhiteSpace(command.ParentRoleSlug)
                ? null
                : command.ParentRoleSlug.ToLower();


            role.Name = command.Name;
            role.Description = command.Description;
            role.ParentRoleSlug = normalizedParentRoleSlug;
            role.LastUpdatedDate = DateTime.Now;
            role.LastUpdatedBy = blocksContext?.UserId;
            role.CanCreateOwn = command.CanCreateOwn;

            if (!string.IsNullOrWhiteSpace(command.ParentRoleSlug))
            {
                var ancestorRoleSlugs = new List<string>() { command.ParentRoleSlug.ToLower() };
                while (true)
                {
                    var roleParent = await _resourceRepository.GetRoleBySlugAsync(command.ParentRoleSlug.ToLower());
                    if (string.IsNullOrWhiteSpace(roleParent.ParentRoleSlug))
                    {
                        break;
                    }
                    else
                    {
                        ancestorRoleSlugs.Add(roleParent.ParentRoleSlug);
                    }
                }
                role.AncestorRoleSlugs = ancestorRoleSlugs;
            }

            var result = await _resourceRepository.UpdateRoleAsync(role);

            if (!result)
            {
                _logger.LogInformation("Role update end -- Error");
                return new BaseMutationResponse();
            }

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Update,
                    ItemId = role.ItemId,
                    Entity = ResourceEntity.Role
                }
            );

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (tenantConfig.IsMultiOrgEnabled & command.PropagateToOtherOrg)
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamOrgQueue,
                    new PropagationRolePermissionUpdateEvent
                    {
                        Entity = "role",
                        ItemId = role.ItemId,
                        Action = "update"
                    }
                );
            }

            _logger.LogInformation("Role update end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = role.ItemId
            };
        }

        public async Task SendResourceMutationEventAsync(ResourceMutationEvent resourceMutation)
        {
            _logger.LogInformation("Permission event -- initiate");
            await _identityAccessManagementService.SendToQueueAsync(
                IdpConstants.IamQueue,
                resourceMutation
            );
            _logger.LogInformation("Permission event -- sent");
        }

        public async Task<SetRolesResponse> SetRolesAsync(SetRolesRequest command)
        {
            _logger.LogInformation("SetRole start");
            if (string.IsNullOrWhiteSpace(command.Slug))
            {
                _logger.LogError("Slug should not be empty or null");
                return new SetRolesResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Slug", "Slug_Never_Empty" }
                    }
                };
            }

            var isExist = await _resourceRepository.GetRoleBySlugAsync(command.Slug);

            if (isExist == null)
            {
                _logger.LogError("Role does not exist by this slug");
                return new SetRolesResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Role", "Role_Not_Found" }
                    }
                };
            }

            if (isExist.CreatedFromDefault)
            {
                _logger.LogWarning("SetRole forbidden for default-derived role slug {Slug}", command.Slug);
                return new SetRolesResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "forbidden", "Can_Not_Change_Default_role" }
                    }
                };
            }
            var currentOrganizationId = ResolveOrganizationId(command?.OragnizationId ?? "");

            if (command.AddPermissions.Any())
            {
                await _resourceRepository.UpdateRolePermissionByIdsAsync(command.Slug, command.AddPermissions);
            }

            if (command.RemovePermissions.Any())
            {
                await _resourceRepository.RemoveRolePermissionByIdsAsync(command.Slug, command.RemovePermissions, currentOrganizationId);
            }
            
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            await SendResourceSetToPermissionMutationEventAsync(
                new ResourceSetToPermissionMutationEvent
                {
                    Entity = ResourceEntity.Role,
                    AddPermissions = command.AddPermissions,
                    RemovePermissions = command.RemovePermissions,
                    Slug = command.Slug,
                    IsPropagationEnable = tenantConfig.IsMultiOrgEnabled && string.Equals(currentOrganizationId, DefaultOrganizationId, StringComparison.OrdinalIgnoreCase)
                    && (command.AddPermissions.Any() || command.RemovePermissions.Any()),
                });

            _logger.LogInformation("SetRole end");

            return new SetRolesResponse
            {
                Success = true
            };

        }

        public async Task ExecutePropagationRolePermissionUpdateAsync(PropagationRolePermissionUpdateEvent command)
        {
            if (command.Entity == "permission")
            {
                if (command.Action == "insert")
                {
                    await InsertPermissionForAllOrg(command.ItemId);
                }

                if (command.Action == "update")
                {
                    await UpdatePermissionForAllOrg(command.ItemId);
                }
            }

            if (command.Entity == "role")
            {
                if (command.Action == "insert")
                {
                    await InsertRoleForAllOrg(command.ItemId);
                }

                if (command.Action == "update")
                {
                    await UpdateRoleForAllOrg(command.ItemId);
                }
            }
        }

        private async Task<bool> InsertPermissionForAllOrg(string itemId)
        {
            var permission = await _resourceRepository.GetPermissionByIdAsync(itemId);

            if (permission == null)
            {
                _logger.LogWarning("Permission not found. ItemId: {ItemId}", itemId);
                return false;
            }

            var orgIds = (await _resourceRepository.GetOrganizationsAsync(new GetOrganizationsRequest()))
                ?.Organizations?
                .Select(x => x.ItemId)
                .ToList() ?? [];

            if (!orgIds.Any())
            {
                _logger.LogWarning("Organizations are empty");
                return false;
            }

            var permissions = orgIds.Select(orgId => new Permission
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,

                CreatedBy = permission.CreatedBy,
                LastUpdatedBy = permission.LastUpdatedBy,
                Language = permission.Language,

                Name = permission.Name,
                Type = permission.Type,
                Description = permission.Description,
                Resource = permission.Resource,
                ResourceGroup = permission.ResourceGroup,

                IsBuiltIn = permission.IsBuiltIn,
                IsArchived = permission.IsArchived,
                PermissionSeverity = permission.PermissionSeverity,

                OrganizationId = orgId,

                // Create new list instances
                // OrganizationIds = permission.OrganizationIds,
                Tags = permission.Tags,
                DependentPermissions = permission.DependentPermissions,
                Roles = permission.Roles
            }).ToList();

            await _resourceRepository.InsertPermissionsAsync(permissions);

            _logger.LogInformation(
                "Created permission '{Resource}' for {Count} organizations",
                permission.Resource,
                permissions.Count);

            return true;
        }

        private async Task<bool> UpdatePermissionForAllOrg(string itemId)
        {
            var permission = await _resourceRepository.GetPermissionByIdAsync(itemId);

            if (permission == null)
            {
                _logger.LogWarning("Permission not found. ItemId: {ItemId}", itemId);
                return false;
            }

            var permissions = await _resourceRepository.GetPermissionsByResourceAsync(permission.Resource); // org agnostic

            if (permissions == null || !permissions.Any())
            {
                _logger.LogInformation(
                    "No permissions found for resource '{Resource}'",
                    permission.Resource);

                return true;
            }

            if (!permissions.Any())
            {
                return true;
            }

            foreach (var orgPermission in permissions)
            {
                if (permission.OrganizationId == DefaultOrganizationId) continue;

                orgPermission.Name = permission.Name;
                orgPermission.Description = permission.Description;
                orgPermission.Type = permission.Type;
                orgPermission.Resource = permission.Resource;
                orgPermission.ResourceGroup = permission.ResourceGroup;
                orgPermission.IsBuiltIn = permission.IsBuiltIn;
                orgPermission.IsArchived = permission.IsArchived;
                orgPermission.PermissionSeverity = permission.PermissionSeverity;

                orgPermission.Tags = permission.Tags?.ToList() ?? [];
                orgPermission.DependentPermissions = permission.DependentPermissions?.ToList() ?? [];

                orgPermission.LastUpdatedDate = DateTime.UtcNow;
                orgPermission.LastUpdatedBy = permission.LastUpdatedBy;
            }

            await _resourceRepository.UpdatePermissionsAsync(permissions); // org agnostic

            _logger.LogInformation(
                "Updated permission '{Resource}' for {Count} organizations",
                permission.Resource,
                permissions.Count);

            return true;
        }

        private async Task<bool> InsertRoleForAllOrg(string itemId)
        {
            var role = await _resourceRepository.GetRoleByIdAsync(itemId);

            if (role == null)
            {
                _logger.LogWarning("Role not found. ItemId: {ItemId}", itemId);
                return false;
            }

            var orgIds = (await _resourceRepository.GetOrganizationsAsync(new GetOrganizationsRequest()))
                ?.Organizations?
                .Select(x => x.ItemId)
                .ToList() ?? [];

            if (!orgIds.Any())
            {
                _logger.LogWarning("Organizations are empty");
                return false;
            }

            var roles = orgIds.Select(orgId => new Role
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,

                CreatedBy = role.CreatedBy,
                LastUpdatedBy = role.LastUpdatedBy,
                Language = role.Language,

                Name = role.Name,
                Slug = role.Slug,
                Description = role.Description,
                AncestorRoleSlugs = role.AncestorRoleSlugs,
                ParentRoleSlug = role.ParentRoleSlug,

                CanCreateOwn = role.CanCreateOwn,
                Count = role.Count,
                CreatedFromDefault = role.CreatedFromDefault,

                OrganizationId = orgId,
            }).ToList();

            await _resourceRepository.InsertRolesAsync(roles);

            _logger.LogInformation(
                "Created role '{RoleSlug}' for {Count} organizations",
                role.Slug,
                roles.Count);

            return true;
        }

        private async Task<bool> UpdateRoleForAllOrg(string itemId)
        {
            var role = await _resourceRepository.GetRoleByIdAsync(itemId);

            if (role == null)
            {
                _logger.LogWarning("Role not found. ItemId: {ItemId}", itemId);
                return false;
            }

            var roles = await _resourceRepository.GetRolesBySlugAsync(role.Slug); // org agnostic

            if (roles == null || !roles.Any())
            {
                _logger.LogInformation(
                    "No organization roles found for slug '{Slug}'",
                    role.Slug);

                return true;
            }

            var rolesToUpdate = roles
                .Where(x => x.ItemId != role.ItemId && x.CreatedFromDefault)
                .ToList();

            if (!rolesToUpdate.Any())
            {
                return true;
            }

            foreach (var orgRole in rolesToUpdate)
            {
                orgRole.Name = role.Name;
                orgRole.Description = role.Description;
                orgRole.ParentRoleSlug = role.ParentRoleSlug;
                orgRole.CanCreateOwn = role.CanCreateOwn;

                orgRole.AncestorRoleSlugs = role.AncestorRoleSlugs != null
                    ? new List<string>(role.AncestorRoleSlugs)
                    : [];

                orgRole.LastUpdatedDate = DateTime.UtcNow;
                orgRole.LastUpdatedBy = role.LastUpdatedBy;
            }

            await _resourceRepository.UpdateRolesAsync(rolesToUpdate); // org agnostic

            _logger.LogInformation(
                "Updated role '{Slug}' for {Count} organizations",
                role.Slug,
                rolesToUpdate.Count);

            return true;
        }


        public async Task SendResourceSetToPermissionMutationEventAsync(ResourceSetToPermissionMutationEvent resourceMutation)
        {
            _logger.LogInformation("Permission event -- initiate");
            await _identityAccessManagementService.SendToQueueAsync(
                IdpConstants.IamQueue,
                resourceMutation
            );
            _logger.LogInformation("Permission event -- sent");
        }

        public async Task ExecuteResourceMutationCommandAsync(ResourceMutationEvent command)
        {
            _logger.LogInformation("Resource Mutation event -- initiate");

            if (command == null)
            {
                _logger.LogWarning("Received null ResourceMutationEvent.");
                return;
            }

            switch (command.Entity)
            {
                case ResourceEntity.Permission:
                    await ProcessResourceMutationEventAsync(command);
                    break;
                case ResourceEntity.Role:
                    await ProcessRoleAsync(command);
                    break;
                default:
                    _logger.LogWarning("Unhandled MutationEventType: {MutationEventType}", command.Action);
                    break;
            }

            _logger.LogInformation("Resource Mutation event -- done");
        }

        private async Task<bool> ProcessResourceMutationEventAsync(ResourceMutationEvent context)
        {
            _logger.LogInformation("Processing permission timeline for ResourceMutationEvent.");

            var permission = await _resourceRepository.GetPermissionByIdAsync(context.ItemId);
            var blocksContext = BlocksContext.GetContext();
            var timeline = new ResourceTimeline<Permission>
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                CurrentData = permission,
                Event = context.Action.ToString().ToLower(),
                Entity = typeof(Permission).Name.ToLower(),
            };

            var result = await _resourceRepository.SaveResourceTimelineAsync(timeline);

            return result;
        }

        private async Task<bool> ProcessRoleAsync(ResourceMutationEvent context)
        {
            _logger.LogInformation("Processing role timeline for ResourceMutationEvent.");
            var role = await _resourceRepository.GetRoleByIdAsync(context.ItemId);
            var blocksContext = BlocksContext.GetContext();
            var timeline = new ResourceTimeline<Role>
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                CurrentData = role,
                Event = context.Action.ToString().ToLower(),
                Entity = typeof(Role).Name.ToLower(),
            };

            var result = await _resourceRepository.SaveResourceTimelineAsync(timeline);
            await _resourceRepository.UpdateRolesCountAsync(role.Slug);

            return result;
        }

        public async Task<bool> ProcessPermissionAsync(ResourceSetToPermissionMutationEvent command)
        {
            _logger.LogInformation("Processing permission timeline for ResourceMutationEvent.");
            var blocksContext = BlocksContext.GetContext();
            var timelines = new List<ResourceTimeline<Permission>>();
            foreach (var itemId in command.AddPermissions.Union(command.RemovePermissions))
            {
                timelines.Add(await ProcessTimelineAsync(blocksContext, itemId, command.Entity == ResourceEntity.Role ? "roleupdate" : "groupupdate"));
            }

            await _resourceRepository.SaveResourceTimelinesAsync(timelines);

            if (command.Entity == ResourceEntity.Role)
            {
                await _resourceRepository.UpdateRolesCountAsync(command.Slug);
            }

            if (command.IsPropagationEnable)
            {
                await PropagateSetPermissionsAsync(command);
            }

            return true;
        }

        private async Task<bool> PropagateSetPermissionsAsync(ResourceSetToPermissionMutationEvent command)
        {
            var orgIds = (await _resourceRepository.GetOrganizationsAsync(new GetOrganizationsRequest()))
                ?.Organizations?
                .Select(x => x.ItemId)
                .ToList() ?? new List<string>();

            if (!orgIds.Any())
            {
                _logger.LogWarning("Organizations are empty");
                return false;
            }

            // Resolve resources only once
            var addResources = command.AddPermissions.Any()
                ? (await _resourceRepository.GetPermissionsByIdsAsync(command.AddPermissions))
                    ?.Select(x => x.Resource)
                    .Distinct()
                    .ToList() ?? new List<string>()
                : new List<string>();

            var removeResources = command.RemovePermissions.Any()
                ? (await _resourceRepository.GetPermissionsByIdsAsync(command.RemovePermissions))
                    ?.Select(x => x.Resource)
                    .Distinct()
                    .ToList() ?? new List<string>()
                : new List<string>();

            foreach (var orgId in orgIds)
            {
                if (addResources.Any())
                {
                    var addPermissionIds =
                        (await _resourceRepository.GetPermissionsByResourcesAsync(addResources, orgId))
                        ?.Select(x => x.ItemId)
                        .ToList() ?? new List<string>();

                    if (addPermissionIds.Any())
                    {
                        await _resourceRepository.UpdateRolePermissionByIdsAsync(
                            command.Slug,
                            addPermissionIds);
                    }
                }

                if (removeResources.Any())
                {
                    var removePermissionIds =
                        (await _resourceRepository.GetPermissionsByResourcesAsync(removeResources, orgId))
                        ?.Select(x => x.ItemId)
                        .ToList() ?? new List<string>();

                    if (removePermissionIds.Any())
                    {
                        await _resourceRepository.RemoveRolePermissionByIdsAsync(
                            command.Slug,
                            removePermissionIds);
                    }
                }
            }

            return true;
        }

        private async Task<ResourceTimeline<Permission>> ProcessTimelineAsync(BlocksContext blocksContext, string itemId, string eventname)
        {
            var permission = await _resourceRepository.GetPermissionByIdAsync(itemId);

            return new ResourceTimeline<Permission>
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                CurrentData = permission,
                Event = eventname,
                Entity = typeof(Permission).Name.ToLower(),
            };
        }

        public async Task<BaseMutationResponse> CreateOrganizationAsync(CreateOrganizationRequest request, string? creatorId = null)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            if (!tenantConfig.IsMultiOrgEnabled)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "multi_org_disabled", "Organization creation is disabled because multi-organization mode is off." }
                    }
                };
            }

            if (request.CreatedFrom == CreatedFrom.Cloud && !tenantConfig.AllowOrgCreationFromCloud)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "org_creation_disabled", "Organization creation is disabled from cloud." }
                    }
                };
            }

            if (request.CreatedFrom == CreatedFrom.ConstructSignup && !tenantConfig.AllowOrgCreationFromSignup)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "org_creation_disabled", "Organization creation is disabled from Construct Signup." }
                    }
                };
            }

            if (request.CreatedFrom == CreatedFrom.ConstructPortal && !tenantConfig.AllowOrgCreationFromPortal)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "org_creation_disabled", "Organization creation is disabled from Construct Portal." }
                    }
                };
            }

            var existingOrganization = await _resourceRepository.GetOrganizationByNameAsync(request.Name);
            if (existingOrganization != null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "name_already_exists", "Organization with same name already exists." }
                    }
                };
            }

            // Create organization
            var contextUserId = BlocksContext.GetContext()?.UserId;
            var createdByUserId = creatorId ?? contextUserId;
            var organization = new Organization
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = createdByUserId,
                CreatedDate = DateTime.UtcNow,
                Name = request.Name,
                DefaultRoleForMembers = request.DefaultRoleForMembers,
                DefaultPermissionsForMembers = request.DefaultPermissionsForMembers,
                LastUpdatedDate = DateTime.UtcNow,
                LastUpdatedBy = createdByUserId,
                Description = request.Description,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                WebsiteUrl = request.WebsiteUrl,
                Addresses = request.Addresses,
                Attributes = request.Attributes,
            };

            await _resourceRepository.SaveOrganizationAsync(organization);

            await _identityAccessManagementService.SendToQueueAsync(
                IdpConstants.IamOrgQueue,
                new OrganizationProvisioningEvent
                {
                    OrganizationId = organization.ItemId,
                    UserId = creatorId ?? contextUserId
                }
            );

            if (request.CreatedFrom == CreatedFrom.ConstructSignup && tenantConfig.AllowOrgCreationFromSignup)
            {
                return new BaseMutationResponse { IsSuccess = true, ItemId = organization.ItemId };
            }

            if (request.CreatedFrom == CreatedFrom.ConstructPortal && tenantConfig.AllowOrgCreationFromPortal)
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamQueue,
                    new UpdateOrganizationUserEvent
                    {
                        OrganizationId = organization.ItemId,
                        UserId = creatorId ?? contextUserId,
                        Roles = request.DefaultRoleForMembers,
                        Permissions = request.DefaultPermissionsForMembers
                    }
                );
            }

            return new BaseMutationResponse { IsSuccess = true, ItemId = organization.ItemId };
        }


        public async Task ExecuteOrganizationProvisioningAsync(OrganizationProvisioningEvent command)
        {
            _logger.LogInformation("Organization provisioning start for OrganizationId: {OrganizationId}", command.OrganizationId);
            var copyRoleResult = await CopyRoleFromDefault(command.OrganizationId, command.UserId);
            var copyPermissionResult = await CopyPermissionsFromDefault(command.OrganizationId, command.UserId);
            if (copyRoleResult && copyPermissionResult)
            {
                _logger.LogInformation("Organization provisioning completed successfully for OrganizationId: {OrganizationId}", command.OrganizationId);
            }
            else
            {
                _logger.LogError("Organization provisioning encountered errors for OrganizationId: {OrganizationId}", command.OrganizationId);
            }
        }

        private async Task<bool> CopyRoleFromDefault(string organizationId, string userId)
        {
            var defaultRoles = await _resourceRepository.GetRolesByOrgAsync(DefaultOrganizationId);
            foreach (var role in defaultRoles)
            {
                role.ItemId = Guid.NewGuid().ToString();
                role.OrganizationId = organizationId;
                role.CreatedBy = userId;
                role.CreatedDate = DateTime.UtcNow;
                role.LastUpdatedBy = userId;
                role.LastUpdatedDate = DateTime.UtcNow;
                role.CreatedFromDefault = true;
            }
            if (defaultRoles.Any())
            {
                await _resourceRepository.InsertRolesAsync(defaultRoles);
            }
            return true;
        }

        private async Task<bool> CopyPermissionsFromDefault(string organizationId, string userId)
        {
            const int batchSize = 100;
            var pageNumber = 1;

            while (true)
            {
                var permissionsToAssign = await _resourceRepository.GetPermissionsByOrgAsync(DefaultOrganizationId, pageNumber, batchSize);
                if (permissionsToAssign.Count == 0)
                {
                    break;
                }

                foreach (var permission in permissionsToAssign)
                {
                    permission.ItemId = Guid.NewGuid().ToString();
                    permission.LastUpdatedBy = userId;
                    permission.LastUpdatedDate = DateTime.UtcNow;
                    permission.OrganizationId = organizationId;
                }

                await _resourceRepository.InsertPermissionsAsync(permissionsToAssign);

                if (permissionsToAssign.Count < batchSize)
                {
                    break;
                }

                pageNumber++;
            }

            return true;
        }

        public async Task<BaseResponse> UpdateOrganizationAsync(string id, SaveOrganizationRequest request)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (!tenantConfig.IsMultiOrgEnabled)
            {
                return new GetOrganizationsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "multi_org_disabled", "Multi-organization mode is disabled." }
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "invalid_request", "Organization ID is required" }
                    }
                };
            }

            var organization = await _resourceRepository.GetOrganizationById(id);
            if (organization == null)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "not_found", "Organization not found" }
                    }
                };
            }

            var userId = BlocksContext.GetContext()?.UserId;
            organization.LastUpdatedDate = DateTime.UtcNow;
            organization.LastUpdatedBy = userId;

            ApplyProperty(request.Name, value => organization.Name = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Description, value => organization.Description = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.DefaultRoleForMembers, value => organization.DefaultRoleForMembers = value, v => v?.Count > 0);
            ApplyProperty(request.DefaultPermissionsForMembers, value => organization.DefaultPermissionsForMembers = value, v => v?.Count > 0);
            ApplyProperty(request.Email, value => organization.Email = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.PhoneNumber, value => organization.PhoneNumber = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.WebsiteUrl, value => organization.WebsiteUrl = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Addresses, value => organization.Addresses = value, v => v?.Count > 0);
            ApplyProperty(request.Attributes, value => organization.Attributes = value, v => v?.Count > 0);
            ApplyProperty(request.Theme, value => organization.Theme = value, v => v != null);
            ApplyProperty(request.LogoUrl, value => organization.LogoUrl = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.LogoId, value => organization.LogoId = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Locale, value => organization.Locale = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.TimeFormat, value => organization.TimeFormat = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.DateFormat, value => organization.DateFormat = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Currency, value => organization.Currency = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.TimeZone, value => organization.TimeZone = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Industry, value => organization.Industry = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.IsEnable, value => organization.IsDisabled = !(value ?? false), v => v.HasValue);

            await _resourceRepository.SaveOrganizationAsync(organization);
            return new BaseResponse { IsSuccess = true };
        }

        private static void ApplyProperty<T>(T? value, Action<T> apply, Func<T?, bool> isValid)
        {
            if (isValid(value))
                apply(value!);
        }


        public async Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (!tenantConfig.IsMultiOrgEnabled)
            {
                return new GetOrganizationsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "multi_org_disabled", "Multi-organization mode is disabled." }
                    }
                };
            }

            var response = await _resourceRepository.GetOrganizationsAsync(request);
            return response;
        }

        public async Task<GetOrganizationResponse> GetOrganizationAsync(string id)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (!tenantConfig.IsMultiOrgEnabled)
            {
                return new GetOrganizationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "multi_org_disabled", "Multi-organization mode is disabled." }
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return new GetOrganizationResponse
                {
                    IsSuccess = false,
                    Organization = null,
                    Errors = new Dictionary<string, string>                    {
                        { "invalid_request", "Organization ID is required" }
                    }
                };
            }

            var organization = await _resourceRepository.GetOrganizationById(id);
            return new GetOrganizationResponse { IsSuccess = true, Organization = organization };
        }

        public async Task<GetMyOrganizationsResponse> GetMyOrganizationAsync()
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (!tenantConfig.IsMultiOrgEnabled)
            {
                return new GetMyOrganizationsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "multi_org_disabled", "Multi-organization mode is disabled." }
                    }
                };
            }

            var userId = BlocksContext.GetContext()?.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new GetMyOrganizationsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "invalid_request", "User ID is required" }
                    }
                };
            }

            var organizationIds = await _resourceRepository.GetOrganizationIdsByUserIdAsync(userId);
            if (organizationIds.Count == 0)
            {
                return new GetMyOrganizationsResponse
                {
                    IsSuccess = true,
                    Organizations = []
                };
            }

            var organizations = await _resourceRepository.GetOrganizationsByIdsAsync(organizationIds);
            var myOrganizations = organizations
                .Select(x => new MyOrganizationInfo
                {
                    ItemId = x.ItemId,
                    Name = x.Name,
                    CreatedDate = x.CreatedDate
                })
                .OrderBy(x => organizationIds.IndexOf(x.ItemId))
                .ToList();

            return new GetMyOrganizationsResponse
            {
                IsSuccess = true,
                Organizations = myOrganizations
            };
        }

        public async Task<BaseResponse> SaveOrganizationConfigAsync(SaveOrganizationConfigRequest request)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (tenantConfig == null)
            {
                tenantConfig = new TenantConfiguration
                {
                    ItemId = Guid.NewGuid().ToString(),
                    CreatedBy = BlocksContext.GetContext()?.UserId,
                    CreatedDate = DateTime.UtcNow
                };
            }

            tenantConfig.AllowOrgCreationFromCloud = request.AllowOrgCreationFromCloud;
            tenantConfig.AllowOrgCreationFromConstruct = request.AllowOrgCreationFromConstruct;
            tenantConfig.AllowOrgCreationFromSignup = request.AllowOrgCreationFromSignup;
            tenantConfig.AllowOrgCreationFromPortal = request.AllowOrgCreationFromPortal;

            tenantConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            tenantConfig.LastUpdatedDate = DateTime.UtcNow;

            if (!tenantConfig.ConsentForMultiOrgEnable && request.IsMultiOrgEnabled && request.ConsentForMultiOrgEnable)
            {
                tenantConfig.IsMultiOrgEnabled = true;
                tenantConfig.ConsentForMultiOrgEnable = true;
                tenantConfig.ConsentTimeForMultiOrgEnable = DateTime.UtcNow;
            }

            await _resourceRepository.SaveOrganizationConfig(tenantConfig);

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<Dictionary<string, object>> GetOrganizationConfigAsync()
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            return new Dictionary<string, object>
            {
                { "allowOrgCreationFromCloud", tenantConfig?.AllowOrgCreationFromCloud ?? false },
                { "allowOrgCreationFromConstruct", tenantConfig?.AllowOrgCreationFromConstruct ?? false },
                { "allowOrgCreationFromSignup", tenantConfig?.AllowOrgCreationFromSignup ?? false },
                { "allowOrgCreationFromPortal", tenantConfig?.AllowOrgCreationFromPortal ?? false },
                { "isMultiOrgEnabled", tenantConfig?.IsMultiOrgEnabled ?? false },
                { "consentForMultiOrgEnable", tenantConfig?.ConsentForMultiOrgEnable ?? false },
                { "itemId", tenantConfig?.ItemId ?? "" }
            };
        }

        private static string ResolveOrganizationId(string organizationId)
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
