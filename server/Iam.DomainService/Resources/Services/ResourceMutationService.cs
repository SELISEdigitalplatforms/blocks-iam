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
                OrganizationId = "default" // Permission is global by default, can be updated later if needed
                
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
            };

            await _resourceRepository.InsertRoleAsync(role);

            return role.ItemId;

        }

        public async Task<BaseMutationResponse> UpdatePermissionAsync(string id, UpdatePermissionRequest command)
        {
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

            var blocksContext = BlocksContext.GetContext();

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

        public async Task<BaseMutationResponse> UpdateRoleAsync(string id, UpdateRoleRequest command)
        {
            _logger.LogInformation("Role update start");
            var role = await _resourceRepository.GetRoleByIdAsync(id);
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

            role.Name = command.Name;
            role.Description = command.Description;
            role.LastUpdatedDate = DateTime.Now;
            role.LastUpdatedBy = blocksContext?.UserId;

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
                Constants.IamQueue,
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

            if (command.AddPermissions.Any())
            {
                await _resourceRepository.UpdateRolePermissionByIdsAsync(command.Slug, command.AddPermissions);
            }

            if (command.RemovePermissions.Any())
            {
                await _resourceRepository.RemoveRolePermissionByIdsAsync(command.Slug, command.RemovePermissions);
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

        public async Task SendResourceSetToPermissionMutationEventAsync(ResourceSetToPermissionMutationEvent resourceMutation)
        {
            _logger.LogInformation("Permission event -- initiate");
            await _identityAccessManagementService.SendToQueueAsync(
                Constants.IamQueue,
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

            if(request.Groups != null && request.Groups.Any())
            {
                const int pageSize = 50;
                int pageNumber = 0;
                
                while (true)
                {
                    var rolePermissions = await _resourceRepository.GetPermissionsByGroupsAsync(request.Groups, DefaultOrganizationId, pageNumber, pageSize);
                    
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
                        rp.Roles = new List<string>();
                    }

                    await _resourceRepository.InsertPermissionsAsync(rolePermissions);
                    
                    if (rolePermissions.Count < pageSize)
                    {
                        break;
                    }
                    
                    pageNumber++;
                }
            }

            if(request.Permissions != null && request.Permissions.Any())
            {
                var permissions = await _resourceRepository.GetPermissionsByIdsAsync(request.Permissions);
                foreach (var permission in permissions)
                {
                    permission.ItemId = Guid.NewGuid().ToString();
                    permission.LastUpdatedBy = organization.CreatedBy;
                    permission.LastUpdatedDate = DateTime.UtcNow;
                    permission.OrganizationId = organization.ItemId; // Assign to new org
                    permission.Roles = new List<string>();
                }

                await _resourceRepository.InsertPermissionsAsync(permissions);
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

            var defaultRoles = await _resourceRepository.GetRolesBySlugAndOrgAsync(request.Roles, DefaultOrganizationId);
            foreach (var role in defaultRoles)
            {
                role.ItemId = Guid.NewGuid().ToString();
                role.OrganizationId = organization.ItemId; // Assign to new org
                role.CreatedBy = organization.CreatedBy;
                role.CreatedDate = DateTime.UtcNow;
                role.LastUpdatedBy = organization.CreatedBy;
                role.LastUpdatedDate = DateTime.UtcNow;
            }

            await _resourceRepository.InsertRolesAsync(defaultRoles);

            const int pageSize = 50;
            int pageNumber = 0;
            
            while (true)
            {
                var rolePermissions = await _resourceRepository.GetPermissionsByRolesAsync(request.Roles, DefaultOrganizationId, pageNumber, pageSize);
                
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
                    rp.Roles = (rp?.Roles?.Where(r => request.Roles.Contains(r)).ToList()) ?? new List<string>();
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

            if (tenantConfig.DefaultRoleOnOrgCreation != null && tenantConfig.DefaultRoleOnOrgCreation.Any())
            {
                var defaultRoles = await _resourceRepository.GetRolesBySlugAndOrgAsync(tenantConfig.DefaultRoleOnOrgCreation, DefaultOrganizationId);
                foreach (var role in defaultRoles)
                {
                    role.ItemId = Guid.NewGuid().ToString();
                    role.OrganizationId = organization.ItemId; // Assign to new org
                    role.CreatedBy = createdByUserId;
                    role.CreatedDate = DateTime.UtcNow;
                    role.LastUpdatedBy = createdByUserId;
                    role.LastUpdatedDate = DateTime.UtcNow;
                }

                await _resourceRepository.InsertRolesAsync(defaultRoles);

                const int pageSize = 50;
                int pageNumber = 0;
                
                while (true)
                {
                    var rolePermissions = await _resourceRepository.GetPermissionsByRolesAsync(tenantConfig.DefaultRoleOnOrgCreation, DefaultOrganizationId, pageNumber, pageSize);
                    
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
                        rp.Roles = (rp?.Roles?.Where(r => tenantConfig.DefaultRoleOnOrgCreation.Contains(r)).ToList()) ?? new List<string>();
                    }

                    await _resourceRepository.InsertPermissionsAsync(rolePermissions);
                    
                    if (rolePermissions.Count < pageSize)
                    {
                        break;
                    }
                    
                    pageNumber++;
                }
            }

            if(tenantConfig.DefaultPermissionOnOrgCreation != null && tenantConfig.DefaultPermissionOnOrgCreation.Any())
            {
                var permissions = await _resourceRepository.GetPermissionsByResourcesAsync(tenantConfig.DefaultPermissionOnOrgCreation, DefaultOrganizationId);
                foreach (var permission in permissions)
                {
                    permission.ItemId = Guid.NewGuid().ToString();
                    permission.LastUpdatedBy = createdByUserId;
                    permission.LastUpdatedDate = DateTime.UtcNow;
                    permission.OrganizationId = organization.ItemId; // Assign to new org
                    permission.Roles = (permission?.Roles?.Where(r => tenantConfig.DefaultRoleOnOrgCreation.Contains(r)).ToList()) ?? new List<string>();
                }

                await _resourceRepository.InsertPermissionsAsync(permissions);
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
                //     Roles = tenantConfig.DefaultRoleOnOrgCreation,
                //     Permissions = new List<string>()
                // });
            }

            return new BaseMutationResponse { IsSuccess = true, ItemId = organization.ItemId };
        }

        public async Task<BaseResponse> UpdateOrganizationAsync(string id, SaveOrganizationRequest request)
        {
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
            var response = await _resourceRepository.GetOrganizationsAsync(request);
            return response;
        }

        public async Task<GetOrganizationResponse> GetOrganizationAsync(string id)
        {
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

        public async Task<BaseResponse> SaveOrganizationConfigAsync(SaveOrganizationConfigRequest request)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (tenantConfig == null)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "invalid_request", "Organization configuration not found" }
                    }
                };
            }
            tenantConfig.AllowOrgCreationFromCloud = request.AllowOrgCreationFromCloud;
            tenantConfig.AllowOrgCreationFromConstruct = request.AllowOrgCreationFromConstruct;
            tenantConfig.AllowOrgCreationFromSignup = request.AllowOrgCreationFromSignup;
            tenantConfig.AllowOrgCreationFromPortal = request.AllowOrgCreationFromPortal;
            tenantConfig.IsMultiOrgEnabled = request.IsMultiOrgEnabled;
            tenantConfig.DefaultRoleOnOrgCreation = request.DefaultRoleOnOrgCreation;
            tenantConfig.DefaultPermissionOnOrgCreation = request.DefaultPermissionOnOrgCreation;
            tenantConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            tenantConfig.LastUpdatedDate = DateTime.UtcNow;
            
            await _resourceRepository.SaveOrganizationConfig(tenantConfig);

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<Dictionary<string, object>> GetOrganizationConfigAsync()
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            return new Dictionary<string, object>
            {
                { "AllowOrgCreationFromCloud", tenantConfig?.AllowOrgCreationFromCloud ?? false },
                { "AllowOrgCreationFromConstruct", tenantConfig?.AllowOrgCreationFromConstruct ?? false },
                { "AllowOrgCreationFromSignup", tenantConfig?.AllowOrgCreationFromSignup ?? false },
                { "AllowOrgCreationFromPortal", tenantConfig?.AllowOrgCreationFromPortal ?? false },
                { "IsMultiOrgEnabled", tenantConfig?.IsMultiOrgEnabled ?? false },
                { "DefaultRoleOnOrgCreation", tenantConfig?.DefaultRoleOnOrgCreation ?? new List<string>() },
                { "DefaultPermissionOnOrgCreation", tenantConfig?.DefaultPermissionOnOrgCreation ?? new List<string>() }
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
