using Authentication.DomainService.Utilities;
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
            };

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
                        { "ItemId", "Item not found" }
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

            if (role.CreatedFromDefault)
            {
                var isDescriptionChanged = !string.Equals(command.Description, role.Description, StringComparison.Ordinal);
                var isParentRoleChanged = !string.Equals(normalizedParentRoleSlug, role.ParentRoleSlug, StringComparison.Ordinal);

                if (isDescriptionChanged || isParentRoleChanged)
                {
                    return new BaseMutationResponse
                    {
                        Errors = new Dictionary<string, string>
                        {
                            { "forbidden", "Only role name can be changed for default-derived organization roles." }
                        }
                    };
                }
            }

            role.Name = command.Name;
            if (!role.CreatedFromDefault)
            {
                role.Description = command.Description;
                role.ParentRoleSlug = normalizedParentRoleSlug;
            }
            role.LastUpdatedDate = DateTime.Now;
            role.LastUpdatedBy = blocksContext?.UserId;

            var result = await _resourceRepository.UpdateRoleAsync(role);

            if (!result)
            {
                _logger.LogInformation("Role update end -- Error");
                return new BaseMutationResponse();
            }

            if (string.Equals(role.OrganizationId, DefaultOrganizationId, StringComparison.OrdinalIgnoreCase))
            {
                await _resourceRepository.UpdateDefaultDerivedRolesBySlugAsync(role);
            }

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Update,
                    ItemId = role.ItemId,
                    Entity = ResourceEntity.Role
                }
            );

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
                return new SetRolesResponse();
            }

            var isExist = await _resourceRepository.GetRoleBySlugAsync(command.Slug);

            if (isExist == null)
            {
                _logger.LogError("Role does not exist by this slug");
                return new SetRolesResponse();
            }

            if (isExist.CreatedFromDefault)
            {
                _logger.LogWarning("SetRole forbidden for default-derived role slug {Slug}", command.Slug);
                return new SetRolesResponse();
            }

            if (command.AddPermissions.Any())
            {
                await _resourceRepository.UpdateRolePermissionByIdsAsync(command.Slug, command.AddPermissions);
            }

            if (command.RemovePermissions.Any())
            {
                await _resourceRepository.RemoveRolePermissionByIdsAsync(command.Slug, command.RemovePermissions);
            }

            var currentOrganizationId = ResolveOrganizationId(BlocksContext.GetContext()?.OrganizationId);
            if (string.Equals(currentOrganizationId, DefaultOrganizationId, StringComparison.OrdinalIgnoreCase)
                && (command.AddPermissions.Any() || command.RemovePermissions.Any()))
            {
                await PropagateDefaultRolePermissionChangesAsync(command.Slug, command.AddPermissions, command.RemovePermissions);
            }

            await SendResourceSetToPermissionMutationEventAsync(
                new ResourceSetToPermissionMutationEvent
                {
                    Entity = ResourceEntity.Role,
                    AddPermissions = command.AddPermissions,
                    RemovePermissions = command.RemovePermissions,
                    Slug = command.Slug
                });

            _logger.LogInformation("SetRole end");

            return new SetRolesResponse
            {
                Success = true
            };

        }

        private async Task PropagateDefaultRolePermissionChangesAsync(string roleSlug, List<string> addPermissionIds, List<string> removePermissionIds)
        {
            var derivedRoles = await _resourceRepository.GetDefaultDerivedRolesBySlugAsync(roleSlug);
            if (!derivedRoles.Any())
            {
                return;
            }

            var addResources = await GetPermissionResourcesByIdsAsync(addPermissionIds);
            var removeResources = await GetPermissionResourcesByIdsAsync(removePermissionIds);

            if (!addResources.Any() && !removeResources.Any())
            {
                return;
            }

            var organizationIds = derivedRoles
                .Select(r => r.OrganizationId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var organizationId in organizationIds)
            {
                if (addResources.Any())
                {
                    await _resourceRepository.AddRoleToPermissionsByResourcesAsync(roleSlug, addResources, organizationId);
                }

                if (removeResources.Any())
                {
                    await _resourceRepository.RemoveRoleFromPermissionsByResourcesAsync(roleSlug, removeResources, organizationId);
                }
            }
        }

        private async Task<List<string>> GetPermissionResourcesByIdsAsync(List<string> permissionIds)
        {
            if (permissionIds == null || permissionIds.Count == 0)
            {
                return new List<string>();
            }

            var permissions = await _resourceRepository.GetPermissionsByIdsAsync(permissionIds);
            return permissions
                .Where(p => !string.IsNullOrWhiteSpace(p.Resource))
                .Select(p => p.Resource)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        public async Task<BaseResponse> AssignPermissionsToOrganizationAsync(AssignPermissionsToOrganizationRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.OrganizationId))
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

            var blocksContext = BlocksContext.GetContext();
            var is_allow = string.IsNullOrWhiteSpace(blocksContext?.OrganizationId) || blocksContext?.OrganizationId == DefaultOrganizationId;
            if (!is_allow)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "forbidden", "Not allowed to assign permissions to organization" }
                    }
                };
            }

            var organization = await _resourceRepository.GetOrganizationById(request.OrganizationId);
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

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            var roleSlugsForPermissionBinding = await ResolveRolesForOrganizationProvisioningAsync(tenantConfig);
            var roleSlugSet = roleSlugsForPermissionBinding.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var defaultPermissions = tenantConfig.KeepOrgPermissionsSameAsDefaultPermissions
                ? await _resourceRepository.GetPermissionsByOrgAsync(DefaultOrganizationId)
                : (tenantConfig.DefaultPermissionsOnOrgCreation.Any()
                    ? await _resourceRepository.GetPermissionsByResourcesAsync(tenantConfig.DefaultPermissionsOnOrgCreation, DefaultOrganizationId)
                    : new List<Permission>());

            if (defaultPermissions.Any())
            {
                foreach (var permission in defaultPermissions)
                {
                    permission.ItemId = Guid.NewGuid().ToString();
                    permission.LastUpdatedBy = organization.CreatedBy;
                    permission.LastUpdatedDate = DateTime.UtcNow;
                    permission.OrganizationId = organization.ItemId;
                    permission.Roles = (permission.Roles ?? new List<string>())
                        .Where(roleSlugSet.Contains)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                await _resourceRepository.InsertPermissionsAsync(defaultPermissions);
            }

            return new BaseResponse
            {
                IsSuccess = true
            };
        }

        public async Task<BaseResponse> AssignRolesToOrganizationAsync(AssignRolesToOrganizationRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.OrganizationId))
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

            var blocksContext = BlocksContext.GetContext();
            var is_allow = string.IsNullOrWhiteSpace(blocksContext?.OrganizationId) || blocksContext?.OrganizationId == DefaultOrganizationId;
            if (!is_allow)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "forbidden", "Not allowed to assign permissions to organization" }
                    }
                };
            }

            var organization = await _resourceRepository.GetOrganizationById(request.OrganizationId);
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

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            var roleSlugs = await ResolveRolesForOrganizationProvisioningAsync(tenantConfig);
            if (!roleSlugs.Any())
            {
                return new BaseResponse { IsSuccess = true };
            }

            var defaultRoles = await _resourceRepository.GetRolesBySlugAndOrgAsync(roleSlugs, DefaultOrganizationId);
            var selectedRoleSlugs = defaultRoles.Select(x => x.Slug).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var role in defaultRoles)
            {
                role.ItemId = Guid.NewGuid().ToString();
                role.OrganizationId = organization.ItemId; // Assign to new org
                role.CreatedBy = organization.CreatedBy;
                role.CreatedDate = DateTime.UtcNow;
                role.LastUpdatedBy = organization.CreatedBy;
                role.LastUpdatedDate = DateTime.UtcNow;
                role.CreatedFromDefault = true;
            }

            if (defaultRoles.Any())
            {
                await _resourceRepository.InsertRolesAsync(defaultRoles);
            }

            const int pageSize = 50;
            int pageNumber = 1;
            
            while (true)
            {
                var rolePermissions = await _resourceRepository.GetPermissionsByRolesAsync(roleSlugs, DefaultOrganizationId, pageNumber, pageSize);
                
                if (rolePermissions == null || rolePermissions.Count == 0)
                {
                    break;
                }
                
                foreach (var rp in rolePermissions) 
                {
                    rp.ItemId = Guid.NewGuid().ToString();
                    rp.LastUpdatedBy = organization.CreatedBy;
                    rp.LastUpdatedDate = DateTime.UtcNow;
                    rp.OrganizationId = organization.ItemId; // Assign to new org
                    rp.Roles = (rp?.Roles?.Where(selectedRoleSlugs.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList()) ?? new List<string>();
                }

                await _resourceRepository.InsertPermissionsAsync(rolePermissions);
                
                if (rolePermissions.Count < pageSize)
                {
                    break;
                }
                
                pageNumber++;
            }
            

            return new BaseResponse
            {
                IsSuccess = true
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

            // Create organization
            var contextUserId = BlocksContext.GetContext()?.UserId;
            var createdByUserId = creatorId ?? contextUserId;
            var organization = new Organization
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = createdByUserId,
                CreatedDate = DateTime.UtcNow,
                Name = request.Name,
                IsEnable = true,
                DefaultRoleForMembers = request.DefaultRoleForMembers,
                DefaultPermissionsForMembers = request.DefaultPermissionsForMembers,
                LastUpdatedDate = DateTime.UtcNow,
                LastUpdatedBy = createdByUserId
            };

            var roleSlugs = await ResolveRolesForOrganizationProvisioningAsync(tenantConfig);
            var selectedRoleSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (roleSlugs.Any())
            {
                var defaultRoles = await _resourceRepository.GetRolesBySlugAndOrgAsync(roleSlugs, DefaultOrganizationId);
                selectedRoleSlugs = defaultRoles.Select(r => r.Slug).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var role in defaultRoles)
                {
                    role.ItemId = Guid.NewGuid().ToString();
                    role.OrganizationId = organization.ItemId; // Assign to new org
                    role.CreatedBy = createdByUserId;
                    role.CreatedDate = DateTime.UtcNow;
                    role.LastUpdatedBy = createdByUserId;
                    role.LastUpdatedDate = DateTime.UtcNow;
                    role.CreatedFromDefault = true;
                }

                if (defaultRoles.Any())
                {
                    await _resourceRepository.InsertRolesAsync(defaultRoles);
                }

                const int pageSize = 50;
                int pageNumber = 1;
                
                while (true)
                {
                    var rolePermissions = await _resourceRepository.GetPermissionsByRolesAsync(roleSlugs, DefaultOrganizationId, pageNumber, pageSize);
                    
                    if (rolePermissions == null || rolePermissions.Count == 0)
                    {
                        break;
                    }
                    
                    foreach (var rp in rolePermissions) 
                    {
                        rp.ItemId = Guid.NewGuid().ToString();
                        rp.LastUpdatedBy = createdByUserId;
                        rp.LastUpdatedDate = DateTime.UtcNow;
                        rp.OrganizationId = organization.ItemId; // Assign to new org
                        rp.Roles = (rp?.Roles?.Where(selectedRoleSlugs.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList()) ?? new List<string>();
                    }

                    await _resourceRepository.InsertPermissionsAsync(rolePermissions);
                    
                    if (rolePermissions.Count < pageSize)
                    {
                        break;
                    }
                    
                    pageNumber++;
                }
            }

            var permissionsToAssign = tenantConfig.KeepOrgPermissionsSameAsDefaultPermissions
                ? await _resourceRepository.GetPermissionsByOrgAsync(DefaultOrganizationId)
                : (tenantConfig.DefaultPermissionsOnOrgCreation != null && tenantConfig.DefaultPermissionsOnOrgCreation.Any()
                    ? await _resourceRepository.GetPermissionsByResourcesAsync(tenantConfig.DefaultPermissionsOnOrgCreation, DefaultOrganizationId)
                    : new List<Permission>());

            if (permissionsToAssign.Any())
            {
                foreach (var permission in permissionsToAssign)
                {
                    permission.ItemId = Guid.NewGuid().ToString();
                    permission.LastUpdatedBy = createdByUserId;
                    permission.LastUpdatedDate = DateTime.UtcNow;
                    permission.OrganizationId = organization.ItemId; // Assign to new org
                    permission.Roles = (permission?.Roles?.Where(selectedRoleSlugs.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList()) ?? new List<string>();
                }

                await _resourceRepository.InsertPermissionsAsync(permissionsToAssign);
            }

            await _resourceRepository.SaveOrganizationAsync(organization);

            if(request.CreatedFrom == CreatedFrom.ConstructSignup && tenantConfig.AllowOrgCreationFromSignup)
            {
                return new BaseMutationResponse { IsSuccess = true, ItemId = organization.ItemId };
            }
            
            if (request.CreatedFrom == CreatedFrom.ConstructPortal && tenantConfig.AllowOrgCreationFromPortal)
            {
                // NOTE: We have to update the user by following code but _userManagementMutationService is in AccountService which will cause circular reference if we inject it here. We can consider to move the user management related code to a separate service to avoid circular reference in the future.
                // await _userManagementMutationService.UpdateOrganizationUserAsync(new UpdateOrganizationUserRequest
                // {
                //     OrganizationId = organization.ItemId,
                //     UserId = creatorId ?? contextUserId,
                //     Roles = tenantConfig.DefaultRolesOnOrgCreation,
                //     Permissions = new List<string>()
                // });
            }

            return new BaseMutationResponse { IsSuccess = true, ItemId = organization.ItemId };
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

            organization.LastUpdatedDate = DateTime.UtcNow;
            organization.Name = request.Name;
            organization.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            organization.IsEnable = request.IsEnable;
            organization.DefaultRoleForMembers = request.DefaultRoleForMembers;
            await _resourceRepository.SaveOrganizationAsync(organization);
            return new BaseResponse { IsSuccess = true };
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
            
            tenantConfig.DefaultRolesOnOrgCreation = request.DefaultRolesOnOrgCreation ?? new List<string>();
            tenantConfig.DefaultPermissionsOnOrgCreation = request.DefaultPermissionsOnOrgCreation ?? new List<string>();
            tenantConfig.KeepOrgRolesSameAsDefaultRoles = request.KeepOrgRolesSameAsDefaultRoles;
            tenantConfig.KeepOrgPermissionsSameAsDefaultPermissions = request.KeepOrgPermissionsSameAsDefaultPermissions;
            tenantConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            tenantConfig.LastUpdatedDate = DateTime.UtcNow;

            if (!tenantConfig.IsMultiOrgEnabled && !tenantConfig.ConsentForMultiOrgEnable && request.IsMultiOrgEnabled && request.ConsentForMultiOrgEnable)
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
                { "defaultRolesOnOrgCreation", tenantConfig?.DefaultRolesOnOrgCreation ?? new List<string>() },
                { "defaultPermissionsOnOrgCreation", tenantConfig?.DefaultPermissionsOnOrgCreation ?? new List<string>() },
                { "keepOrgRolesSameAsDefaultRoles", tenantConfig?.KeepOrgRolesSameAsDefaultRoles ?? true },
                { "keepOrgPermissionsSameAsDefaultPermissions", tenantConfig?.KeepOrgPermissionsSameAsDefaultPermissions ?? true },
                { "itemId", tenantConfig?.ItemId ?? "" }
            };
        }

        private async Task<List<string>> ResolveRolesForOrganizationProvisioningAsync(TenantConfiguration tenantConfig)
        {
            if (tenantConfig.KeepOrgRolesSameAsDefaultRoles)
            {
                var allDefaultRoles = await _resourceRepository.GetRolesByOrgAsync(DefaultOrganizationId);
                return allDefaultRoles
                    .Select(r => r.Slug)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return (tenantConfig.DefaultRolesOnOrgCreation ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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
