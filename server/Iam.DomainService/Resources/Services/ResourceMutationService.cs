using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

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
        private readonly ITenantPermissionPropagator _tenantPermissionPropagator;
        private readonly IUserActivityDispatcher _userActivityDispatcher;

        public ResourceMutationService(
            ILogger<ResourceMutationService> logger,
            IResourceRepository resourceRepository,
            IIdentityAccessManagementService identityAccessManagementService,
            IValidator<CreatePermissionRequest> permissionValidator,
            IValidator<UpdatePermissionRequest> updatepPermissionValidator,
            IValidator<CreateRoleRequest> roleValidator,
            ITenantPermissionPropagator tenantPermissionPropagator,
            IUserActivityDispatcher userActivityDispatcher
        )
        {
            _logger = logger;
            _resourceRepository = resourceRepository;
            _identityAccessManagementService = identityAccessManagementService;
            _permissionValidator = permissionValidator;
            _updatepPermissionValidator = updatepPermissionValidator;
            _roleValidator = roleValidator;
            _tenantPermissionPropagator = tenantPermissionPropagator;
            _userActivityDispatcher = userActivityDispatcher;
        }

        public async Task<BaseMutationResponse> CreatePermissionAsync(CreatePermissionRequest command)
        {
            var blocksContext = BlocksContext.GetContext();
            if (!IsDefaultOrgScope(blocksContext?.OrganizationId))
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


            if (await IsMultiOrgEnabledAsync() && IsDefaultOrgScope(blocksContext?.OrganizationId))
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
            var blocksContext = BlocksContext.GetContext();
            if (!IsDefaultOrgScope(blocksContext?.OrganizationId))
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "forbidden", "Not allowed to create role in this organization" }
                    }
                };
            }

            _logger.LogInformation("Role creation start");

            var validationResult = await _roleValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            // An archived parent is still resolvable by slug -- deliberately, since the duplicate
            // check during organization provisioning depends on that. Hanging a live role off one
            // would build a hierarchy whose parent is hidden from every roles list.
            if (!string.IsNullOrWhiteSpace(command.ParentRoleSlug))
            {
                var parent = await _resourceRepository.GetRoleBySlugAsync(command.ParentRoleSlug.ToLower());
                if (parent?.IsArchived == true)
                {
                    _logger.LogInformation("Role creation end -- Parent Role Archived");
                    return new BaseMutationResponse
                    {
                        Errors = new Dictionary<string, string>
                        {
                            { "ParentRoleSlug", "Parent_Role_Is_Archived" }
                        }
                    };
                }
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

            if (IsDefaultOrgScope(blocksContext?.OrganizationId) && await IsMultiOrgEnabledAsync())
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
            if (!IsDefaultOrgScope(blocksContext?.OrganizationId))
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

            var mutationAction = command.IsArchived
                ? MutationEventType.Delete
                : MutationEventType.Update;

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = mutationAction,
                    ItemId = permission.ItemId,
                    Entity = ResourceEntity.Permission
                }
            );

            if (await IsMultiOrgEnabledAsync() && IsDefaultOrgScope(blocksContext?.OrganizationId))
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamOrgQueue,
                    new PropagationRolePermissionUpdateEvent
                    {
                        Entity = "permission",
                        ItemId = permission.ItemId,
                        Action = command.IsArchived ? "delete" : "update"
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

        public async Task<BaseMutationResponse> ArchivePermissionAsync(string id, bool confirmRevokeFromUsers = false)
        {
            var blocksContext = BlocksContext.GetContext();
            if (!IsDefaultOrgScope(blocksContext?.OrganizationId))
            {
                return Failure("forbidden", "Not_Allowed_To_Archive_Permission_Outside_Default_Organization");
            }

            _logger.LogInformation("Permission archive start");

            var permission = await _resourceRepository.GetPermissionByIdAsync(id);
            if (permission == null)
            {
                _logger.LogInformation("Permission archive end -- Not Found");
                return Failure("ItemId", "Permission_Not_Found");
            }

            // GetPermissionByIdAsync matches on ItemId alone, so a default-org caller can reach a
            // copied organization's record directly. Archiving it here would bypass the
            // propagation-driven archive of copies, so it is refused.
            if (!IsDefaultOrgScope(permission.OrganizationId))
            {
                _logger.LogInformation("Permission archive end -- Not A Default Organization Record");
                return Failure("forbidden", "Permission_Not_A_Default_Organization_Record");
            }

            if (permission.IsArchived)
            {
                _logger.LogInformation("Permission archive end -- Already Archived");
                return Failure("archived", "Permission_Already_Archived");
            }

            if (permission.IsBuiltIn && !_identityAccessManagementService.IsRoot())
            {
                _logger.LogInformation("Permission archive end -- Built In Requires Root Tenant");
                return Failure("forbidden", "Only_Root_Tenant_Can_Archive_Built_In_Permission");
            }

            // Only User.Permissions is scrubbed here, and only under consent. Permission.Roles is
            // cleaned by the archive itself, but it grants nobody anything on its own -- the direct
            // per-user grant is the binding AuthorizationClaimsResolver turns into a token claim,
            // so leaving it behind means an archived permission keeps working for that user
            // indefinitely. Cleanup precedes the archive for the same reason the role path does it
            // in that order: a resource pulled from users without the permission being archived is
            // the safer half-state, and a retry converges.
            if (confirmRevokeFromUsers)
            {
                var usersCleaned = await _resourceRepository.RemovePermissionFromAllUsersAsync(
                    permission.Resource, permission.OrganizationId);

                if (!usersCleaned)
                {
                    _logger.LogWarning(
                        "Permission archive aborted for '{Resource}' in organization '{OrganizationId}': the direct user-grant cleanup was not acknowledged. The permission is left active so a retry can complete it.",
                        permission.Resource,
                        permission.OrganizationId);

                    return new BaseMutationResponse();
                }
            }

            permission.IsArchived = true;
            // UTC so this record's stamp is comparable with the copies archived by
            // DeletePermissionForAllOrg, which already writes UtcNow.
            permission.LastUpdatedDate = DateTime.UtcNow;
            permission.LastUpdatedBy = blocksContext?.UserId;

            var result = await _resourceRepository.UpdatePermissionAsync(permission);

            if (!result)
            {
                _logger.LogInformation("Permission archive end -- Error");
                return new BaseMutationResponse();
            }

            // The cross-organization write is best-effort: the archive of the default-org record
            // has already committed, so a failure here must not fail the request. It is logged
            // rather than swallowed, because the observable result is copies still active in other
            // organizations -- otherwise invisible until someone notices the permission still works.
            var propagated = await _resourceRepository.UpdateAllSamePermissionAsync(permission);

            if (!propagated)
            {
                _logger.LogWarning(
                    "Permission archive: cross-organization propagation was not acknowledged for resource '{Resource}' (ItemId {ItemId}). The default-organization record is archived, but copies in other organizations may still be active and need manual review.",
                    permission.Resource,
                    permission.ItemId);
            }

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Delete,
                    ItemId = permission.ItemId,
                    Entity = ResourceEntity.Permission
                }
            );

            // Null-tolerant on purpose -- the archive has already committed by this point. See
            // IsMultiOrgEnabledAsync for why dereferencing the configuration here would be a 500
            // on a request that actually succeeded.
            if (await IsMultiOrgEnabledAsync() && IsDefaultOrgScope(blocksContext?.OrganizationId))
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamOrgQueue,
                    new PropagationRolePermissionUpdateEvent
                    {
                        Entity = "permission",
                        ItemId = permission.ItemId,
                        Action = "delete",
                        ConfirmRevokeFromUsers = confirmRevokeFromUsers
                    }
                );
            }

            _logger.LogInformation("Permission archive end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = permission.ItemId
            };
        }

        private static BaseMutationResponse Failure(string key, string message)
        {
            return new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { key, message } }
            };
        }

        public async Task<BaseMutationResponse> ArchiveRoleAsync(string id, bool confirmRevokeFromUsers = false)
        {
            var blocksContext = BlocksContext.GetContext();
            var callerOrganizationId = ResolveOrganizationId(blocksContext?.OrganizationId ?? string.Empty);

            _logger.LogInformation("Role archive start");

            var role = await _resourceRepository.GetRoleByIdAsync(id);
            if (role == null)
            {
                _logger.LogInformation("Role archive end -- Not Found");
                return Failure("ItemId", "Role_Not_Found");
            }

            // Copies are retired by propagating the archive of their master record, never directly.
            // Same condition UpdateRoleAsync already uses to protect them.
            if (role.CreatedFromDefault && !IsDefaultOrgScope(role.OrganizationId))
            {
                _logger.LogInformation("Role archive end -- Default Copied Role");
                return Failure("forbidden", "Can_Not_Archive_Default_Copied_Role");
            }

            // Compared for every caller, including default-organization ones. GetRoleByIdAsync has
            // no organization scope, so a default-org caller could otherwise archive another
            // organization's own role by id -- the guard above only catches copies.
            if (role.OrganizationId != callerOrganizationId)
            {
                _logger.LogInformation("Role archive end -- Role Belongs To Another Organization");
                return Failure("forbidden", "Not_Allowed_To_Archive_Role_From_Another_Organization");
            }

            if (role.IsArchived)
            {
                _logger.LogInformation("Role archive end -- Already Archived");
                return Failure("archived", "Role_Already_Archived");
            }

            if (await _resourceRepository.HasChildRolesAsync(role.Slug, role.OrganizationId))
            {
                _logger.LogInformation("Role archive end -- Has Child Roles");
                return Failure("dependency", "Role_Has_Child_Roles");
            }

            // Consent turns a refusal into a recorded revocation. Without it the guard is exactly
            // as it was: an active holder blocks the archive. With it the archive proceeds and the
            // scrub below removes the slug from every user in the organization regardless of
            // state -- which is why the warning is not optional. Revoking live access is the one
            // effect of this endpoint invisible to the caller and impossible to undo (un-archiving
            // is out of scope, so the assignment is gone, not hidden), and this log is its only
            // durable trace.
            if (await _resourceRepository.HasUserAssignmentsAsync(role.Slug, role.OrganizationId))
            {
                if (!confirmRevokeFromUsers)
                {
                    _logger.LogInformation("Role archive end -- Has Active User Assignments");
                    return Failure("dependency", "Role_Has_Active_User_Assignments");
                }

                _logger.LogWarning(
                    "Archiving role '{Slug}' in organization '{OrganizationId}' with explicit consent while at least one active user still holds it. Their assignment is being removed and cannot be restored by un-archiving.",
                    role.Slug,
                    role.OrganizationId);
            }

            // Cleanup precedes the archive deliberately. If the archive write then fails, a slug
            // removed from permissions without the role being archived is the safer half-state and
            // a retry converges. The reverse -- an archived role still referenced -- is not, so an
            // unacknowledged cleanup stops here rather than archiving anyway.
            var permissionsCleaned = await _resourceRepository.RemoveRoleFromAllPermissionsAsync(role.Slug, role.OrganizationId);
            var usersCleaned = await _resourceRepository.RemoveRoleFromAllUsersAsync(role.Slug, role.OrganizationId);

            if (!permissionsCleaned || !usersCleaned)
            {
                _logger.LogWarning(
                    "Role archive aborted for '{Slug}' in organization '{OrganizationId}': reference cleanup was not acknowledged (permissions: {PermissionsCleaned}, users: {UsersCleaned}). The role is left active so a retry can complete it.",
                    role.Slug,
                    role.OrganizationId,
                    permissionsCleaned,
                    usersCleaned);

                return new BaseMutationResponse();
            }

            role.IsArchived = true;
            role.LastUpdatedDate = DateTime.UtcNow;
            role.LastUpdatedBy = blocksContext?.UserId;

            var result = await _resourceRepository.UpdateRoleAsync(role);

            if (!result)
            {
                _logger.LogInformation("Role archive end -- Error");
                return new BaseMutationResponse();
            }

            // Both sends happen after the archive has committed, and a retry cannot republish them
            // because it stops at Role_Already_Archived. Letting an exception escape would return
            // 500 for a write that succeeded and still leave the gap, so the failure is reported
            // here instead, named clearly enough to drive the reconciliation pass the ticket
            // describes. This matters more for roles than for permissions: the permission path
            // updates other organizations' copies synchronously before publishing, whereas for
            // roles this queue message is the only cross-organization channel there is, so losing
            // it leaves every copy active.
            try
            {
                await SendResourceMutationEventAsync(
                    new ResourceMutationEvent
                    {
                        Action = MutationEventType.Delete,
                        ItemId = role.ItemId,
                        Entity = ResourceEntity.Role
                    }
                );

                // Gated on the same resolved organization the guards used: reading the raw context
                // value here instead would let a caller whose context carries no organization
                // archive the master record (resolved to default) while silently skipping
                // propagation, leaving every copy active.
                if (await IsMultiOrgEnabledAsync() && IsDefaultOrgScope(callerOrganizationId))
                {
                    await _identityAccessManagementService.SendToQueueAsync(
                        IdpConstants.IamOrgQueue,
                        new PropagationRolePermissionUpdateEvent
                        {
                            Entity = "role",
                            ItemId = role.ItemId,
                            Action = "delete",
                            // Carried, not re-derived: the consumer runs later and has no way to
                            // know a human agreed to revoke live assignments.
                            ConfirmRevokeFromUsers = confirmRevokeFromUsers
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Role '{Slug}' (ItemId {ItemId}) in organization '{OrganizationId}' was archived, but publishing its events failed. The archive itself is committed and will not be retried -- copies in other organizations may still be active and need a reconciliation pass.",
                    role.Slug,
                    role.ItemId,
                    role.OrganizationId);
            }

            _logger.LogInformation("Role archive end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = role.ItemId
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

            if (role.CreatedFromDefault && !IsDefaultOrgScope(role.OrganizationId))
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

            if (await IsMultiOrgEnabledAsync() && IsDefaultOrgScope(blocksContext?.OrganizationId))
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
                IdpConstants.IamResourceQueue,
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

            var currentOrganizationId = ResolveOrganizationId(command?.OrganizationId ?? "");
            var isExist = await _resourceRepository.GetRoleBySlugAsync(command.Slug, currentOrganizationId);

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

            // Archiving pulls the slug out of every permission in the organization. Without this
            // guard the next assign-permissions call would put it straight back, quietly undoing
            // that cleanup and leaving permissions pointing at a retired role.
            if (isExist.IsArchived)
            {
                _logger.LogWarning("Refusing to change permissions for archived role '{Slug}' in organization '{OrganizationId}'", command.Slug, currentOrganizationId);
                return new SetRolesResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "archived", "Role_Already_Archived" }
                    }
                };
            }


            if (command.AddPermissions.Any())
            {
                await _resourceRepository.UpdateRolePermissionByIdsAsync(command.Slug, command.AddPermissions, currentOrganizationId);
            }

            if (command.RemovePermissions.Any())
            {
                await _resourceRepository.RemoveRolePermissionByIdsAsync(command.Slug, command.RemovePermissions, currentOrganizationId);
            }

            await SendResourceSetToPermissionMutationEventAsync(
                new ResourceSetToPermissionMutationEvent
                {
                    Entity = ResourceEntity.Role,
                    AddPermissions = command.AddPermissions,
                    RemovePermissions = command.RemovePermissions,
                    Slug = command.Slug,
                    OrganizationId = currentOrganizationId,
                    // Carried, not re-derived: the consumer runs later and cannot know what the
                    // request asked for.
                    PropagateToAllOrganizations = command.PropagateToAllOrganizations
                });

            _logger.LogInformation("SetRole end");

            return new SetRolesResponse
            {
                Success = true
            };

        }

        public async Task ExecutePropagationRolePermissionUpdateAsync(PropagationRolePermissionUpdateEvent command)
        {
            if (command == null)
            {
                _logger.LogWarning("Received null PropagationRolePermissionUpdateEvent.");
                return;
            }

            var (entity, action) = (command.Entity?.ToLowerInvariant(), command.Action?.ToLowerInvariant());

            switch (entity, action)
            {
                case ("permission", "insert"):
                    await InsertPermissionForAllOrg(command.ItemId);
                    break;
                case ("permission", "update"):
                    await UpdatePermissionForAllOrg(command.ItemId);
                    break;
                case ("permission", "delete"):
                    await DeletePermissionForAllOrg(command.ItemId, command.ConfirmRevokeFromUsers);
                    break;
                case ("role", "insert"):
                    await InsertRoleForAllOrg(command.ItemId);
                    break;
                case ("role", "update"):
                    await UpdateRoleForAllOrg(command.ItemId);
                    break;
                case ("role", "delete"):
                    await DeleteRoleForAllOrg(command.ItemId, command.ConfirmRevokeFromUsers);
                    break;
                default:
                    _logger.LogWarning(
                        "Unhandled propagation event: Entity={Entity}, Action={Action}",
                        command.Entity,
                        command.Action);
                    break;
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
                if (orgPermission.OrganizationId == DefaultOrganizationId) continue;

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

            var roles = new List<Role>();
            foreach (var orgId in orgIds)
            {
                var existing = await _resourceRepository.GetRoleBySlugAsync(role.Slug, orgId);
                if (existing != null)
                {
                    continue;
                }

                roles.Add(new Role
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
                    AncestorRoleSlugs = role.AncestorRoleSlugs != null
                        ? new List<string>(role.AncestorRoleSlugs)
                        : [],
                    ParentRoleSlug = role.ParentRoleSlug,

                    CanCreateOwn = role.CanCreateOwn,
                    Count = role.Count,
                    CreatedFromDefault = true,

                    OrganizationId = orgId,
                });
            }

            if (!roles.Any())
            {
                _logger.LogInformation(
                    "Role '{Slug}' already exists in all organizations; nothing to insert",
                    role.Slug);
                return true;
            }

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

        private async Task<bool> DeletePermissionForAllOrg(string itemId, bool confirmRevokeFromUsers = false)
        {
            var permission = await _resourceRepository.GetPermissionByIdAsync(itemId);

            if (permission == null)
            {
                _logger.LogWarning("Permission not found for delete propagation. ItemId: {ItemId}", itemId);
                return false;
            }

            var permissions = await _resourceRepository.GetPermissionsByResourceAsync(permission.Resource);

            if (permissions == null || !permissions.Any())
            {
                return true;
            }

            var stale = 0;
            foreach (var orgPermission in permissions)
            {
                if (orgPermission.OrganizationId == DefaultOrganizationId)
                {
                    continue;
                }

                if (orgPermission.IsArchived)
                {
                    continue;
                }

                // Under consent the direct grants are scrubbed per organization too, so the
                // "no user still holds an archived permission" invariant holds everywhere, not
                // only in the organization the administrator was looking at.
                if (confirmRevokeFromUsers)
                {
                    var usersCleaned = await _resourceRepository.RemovePermissionFromAllUsersAsync(
                        orgPermission.Resource, orgPermission.OrganizationId);

                    if (!usersCleaned)
                    {
                        _logger.LogWarning(
                            "Permission archive propagation skipped '{Resource}' in organization '{OrganizationId}': the direct user-grant cleanup was not acknowledged. The copy is left active rather than archived with users still holding it.",
                            orgPermission.Resource,
                            orgPermission.OrganizationId);
                        continue;
                    }
                }

                orgPermission.IsArchived = true;
                orgPermission.LastUpdatedDate = DateTime.UtcNow;
                orgPermission.LastUpdatedBy = permission.LastUpdatedBy;
                stale++;
            }

            if (stale > 0)
            {
                await _resourceRepository.UpdatePermissionsAsync(permissions);
            }

            _logger.LogInformation(
                "Archived permission '{Resource}' for {Count} organizations",
                permission.Resource,
                stale);

            return true;
        }

        private async Task<bool> DeleteRoleForAllOrg(string itemId, bool confirmRevokeFromUsers = false)
        {
            var role = await _resourceRepository.GetRoleByIdAsync(itemId);

            if (role == null)
            {
                _logger.LogWarning("Role not found for delete propagation. ItemId: {ItemId}", itemId);
                return false;
            }

            var roles = await _resourceRepository.GetRolesBySlugAsync(role.Slug);

            if (roles == null || !roles.Any())
            {
                return true;
            }

            var orphaned = roles
                .Where(x => x.ItemId != role.ItemId && x.CreatedFromDefault)
                .ToList();

            if (!orphaned.Any())
            {
                return true;
            }

            var archived = new List<Role>();

            foreach (var orgRole in orphaned)
            {
                // A redelivered queue message must not rewrite copies that are already settled.
                if (orgRole.IsArchived)
                {
                    continue;
                }

                // The same rules that hard-block a direct archive apply per organization: retiring
                // a role platform-wide must not orphan a live assignment in one of them. Checked
                // before any cleanup, so a skipped copy is left completely untouched rather than
                // half-cleaned.
                if (await _resourceRepository.HasChildRolesAsync(orgRole.Slug, orgRole.OrganizationId))
                {
                    _logger.LogWarning(
                        "Role archive propagation skipped '{Slug}' in organization '{OrganizationId}': the copy still has child roles there. Every other organization is unaffected. This message is not replayed and re-archiving the source returns Role_Already_Archived, so retire the children and then archive this copy through a reconciliation pass.",
                        orgRole.Slug,
                        orgRole.OrganizationId);
                    continue;
                }

                // Consent given in the default organization reaches every copy: leaving one active
                // because someone there still holds it is exactly the split-brain state this
                // propagation exists to prevent -- the role would vanish from the administrator's
                // list and keep working in that organization, with no caller able to archive it
                // (C2 refuses a CreatedFromDefault copy directly).
                if (await _resourceRepository.HasUserAssignmentsAsync(orgRole.Slug, orgRole.OrganizationId))
                {
                    if (!confirmRevokeFromUsers)
                    {
                        _logger.LogWarning(
                            "Role archive propagation skipped '{Slug}' in organization '{OrganizationId}': the copy is still assigned to an active user there. Every other organization is unaffected. This message is not replayed and re-archiving the source returns Role_Already_Archived, so remove the assignment and then archive this copy through a reconciliation pass.",
                            orgRole.Slug,
                            orgRole.OrganizationId);
                        continue;
                    }

                    _logger.LogWarning(
                        "Role archive propagation is removing '{Slug}' from at least one active user in organization '{OrganizationId}', under the consent given with the original archive. Their assignment cannot be restored by un-archiving.",
                        orgRole.Slug,
                        orgRole.OrganizationId);
                }

                var permissionsCleaned = await _resourceRepository.RemoveRoleFromAllPermissionsAsync(orgRole.Slug, orgRole.OrganizationId);
                var usersCleaned = await _resourceRepository.RemoveRoleFromAllUsersAsync(orgRole.Slug, orgRole.OrganizationId);

                if (!permissionsCleaned || !usersCleaned)
                {
                    _logger.LogWarning(
                        "Role archive propagation skipped '{Slug}' in organization '{OrganizationId}': reference cleanup was not acknowledged (permissions: {PermissionsCleaned}, users: {UsersCleaned}). The copy is left active rather than archived with dangling references.",
                        orgRole.Slug,
                        orgRole.OrganizationId,
                        permissionsCleaned,
                        usersCleaned);
                    continue;
                }

                orgRole.IsArchived = true;
                orgRole.LastUpdatedDate = DateTime.UtcNow;
                orgRole.LastUpdatedBy = role.LastUpdatedBy;
                archived.Add(orgRole);
            }

            if (archived.Count == 0)
            {
                return true;
            }

            // Only the copies that were actually archived: the source role is written by
            // ArchiveRoleAsync and must never appear in this bulk write.
            var persisted = await _resourceRepository.UpdateRolesAsync(archived);

            if (!persisted)
            {
                _logger.LogWarning(
                    "Role archive propagation for '{Slug}' was not acknowledged for organizations {Organizations}. Those copies may still be active and need manual review.",
                    role.Slug,
                    string.Join(", ", archived.Select(x => x.OrganizationId)));

                return false;
            }

            _logger.LogInformation(
                "Archived role '{Slug}' for {Count} organizations",
                role.Slug,
                archived.Count);

            return true;
        }


        public async Task SendResourceSetToPermissionMutationEventAsync(ResourceSetToPermissionMutationEvent resourceMutation)
        {
            _logger.LogInformation("Permission event -- initiate");
            await _identityAccessManagementService.SendToQueueAsync(
                IdpConstants.IamPermissionQueue,
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
            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = BlocksContext.GetContext()?.UserId ?? permission?.ItemId ?? string.Empty,
                Category = UserActivityCategory.Resource,
                Event = context.Action switch
                {
                    MutationEventType.Create => "PERMISSION_CREATED",
                    MutationEventType.Update => "PERMISSION_UPDATED",
                    MutationEventType.Delete => "PERMISSION_DELETED",
                    _ => context.Action.ToString().ToUpperInvariant()
                },
                Source = "iam-resource-mutation",
                Entity = "Permission",
                EntityId = context.ItemId
            });

            if (permission.IsBuiltIn)
            {
                await _identityAccessManagementService.SendToQueueAsync(
                    IdpConstants.IamPermissionQueue,
                    new PermissionMutationForTenantsEvent
                    {
                        Action = context.Action,
                        ItemId = context.ItemId
                    }
                );
            }

            return true;
        }

        private async Task<bool> ProcessRoleAsync(ResourceMutationEvent context)
        {
            _logger.LogInformation("Processing role timeline for ResourceMutationEvent.");
            var role = await _resourceRepository.GetRoleByIdAsync(context.ItemId);
            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = BlocksContext.GetContext()?.UserId ?? role?.ItemId ?? string.Empty,
                Category = UserActivityCategory.Resource,
                Event = context.Action switch
                {
                    MutationEventType.Create => "ROLE_CREATED",
                    MutationEventType.Update => "ROLE_UPDATED",
                    MutationEventType.Delete => "ROLE_DELETED",
                    _ => context.Action.ToString().ToUpperInvariant()
                },
                Source = "iam-resource-mutation",
                Entity = "Role",
                EntityId = context.ItemId
            });
            await _resourceRepository.UpdateRolesCountAsync(role.Slug);

            return true;
        }

        public async Task ExecutePermissionMutationForTenantsAsync(PermissionMutationForTenantsEvent context)
        {
            _logger.LogInformation(
                "Tenant permission propagation start. ItemId={ItemId} Action={Action}",
                context.ItemId, context.Action);

            var summary = await _tenantPermissionPropagator.PropagateAsync(context);

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = BlocksContext.GetContext()?.UserId ?? string.Empty,
                Category = UserActivityCategory.Resource,
                Event = "PERMISSION_PROPAGATED",
                Source = "iam-permission-propagation",
                Entity = "Permission",
                EntityId = context.ItemId,
                Outcome = summary.TenantsFailed == 0 ? "success" : "partial_failure",
                Metadata = new Dictionary<string, string>
                {
                    { "action", context.Action.ToString() },
                    { "tenantsAttempted", summary.TenantsAttempted.ToString() },
                    { "tenantsSucceeded", summary.TenantsSucceeded.ToString() },
                    { "tenantsFailed", summary.TenantsFailed.ToString() }
                }
            });

            _logger.LogInformation(
                "Tenant permission propagation done. ItemId={ItemId} Action={Action} Attempted={Attempted} Succeeded={Succeeded} Failed={Failed}",
                context.ItemId, context.Action, summary.TenantsAttempted, summary.TenantsSucceeded, summary.TenantsFailed);
        }

        public async Task<bool> ProcessPermissionAsync(ResourceSetToPermissionMutationEvent command)
        {
            _logger.LogInformation("Processing permission timeline for ResourceMutationEvent.");
            var actorUserId = BlocksContext.GetContext()?.UserId ?? string.Empty;
            var eventName = command.Entity == ResourceEntity.Role ? "ROLE_PERMISSIONS_UPDATED" : "GROUP_PERMISSIONS_UPDATED";

            foreach (var itemId in command.AddPermissions.Union(command.RemovePermissions))
            {
                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = actorUserId,
                    Category = UserActivityCategory.Resource,
                    Event = eventName,
                    Source = "iam-resource-mutation",
                    Entity = "Permission",
                    EntityId = itemId
                });
            }

            if (command.Entity == ResourceEntity.Role)
            {
                await _resourceRepository.UpdateRolesCountAsync(command.Slug, command.OrganizationId);
            }

            // Runs last, after the activity records and the role count, so a propagation failure
            // cannot suppress the audit trail of what the administrator actually did in their own
            // organization. Gated on all three conditions: the caller opted in, the tenant is
            // multi-org, and the caller is default-org scoped -- without the last, an
            // organization-scoped admin could rewrite bindings platform-wide.
            if (command.PropagateToAllOrganizations
                && command.Entity == ResourceEntity.Role
                && IsDefaultOrgScope(command.OrganizationId)
                && await IsMultiOrgEnabledAsync())
            {
                await PropagateSetPermissionsAsync(command);
            }

            return true;
        }

        private async Task<bool> PropagateSetPermissionsAsync(ResourceSetToPermissionMutationEvent command)
        {
            var orgIds = await _resourceRepository.GetAllOrgIdsAsync();
                
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
                // SetRoleAsync refuses to modify an archived role in the caller's organization, and
                // propagation must not do through the back door what the synchronous path forbids.
                // This matters more since archive propagation shipped: the two ride different
                // queues (IamOrgQueue vs IamPermissionQueue) with no ordering guarantee, so a
                // consented assignment and a consented archive issued close together can arrive in
                // either order.
                var orgRole = await _resourceRepository.GetRoleBySlugAsync(command.Slug, orgId);

                if (orgRole == null || orgRole.IsArchived)
                {
                    _logger.LogWarning(
                        "Role permission propagation skipped '{Slug}' in organization '{OrganizationId}': the copy is missing or archived there. Every other organization is unaffected.",
                        command.Slug,
                        orgId);
                    continue;
                }

                if (addResources.Any())
                {
                    var addPermissionIds =
                        (await _resourceRepository.GetPermissionsByResourcesAsync(addResources, orgId))
                        ?.Select(x => x.ItemId)
                        .ToList() ?? new List<string>();

                    if (addPermissionIds.Any())
                    {
                        // The result is inspected rather than discarded: an unacknowledged write
                        // leaves this organization silently out of step with the rest, and the log
                        // is the only place that becomes visible. One organization failing must
                        // not abort the others, so this warns and carries on.
                        var added = await _resourceRepository.UpdateRolePermissionByIdsAsync(
                            command.Slug,
                            addPermissionIds,orgId);

                        if (!added)
                        {
                            _logger.LogWarning(
                                "Role permission propagation was not acknowledged when adding permissions to role '{Slug}' in organization '{OrganizationId}'. That organization may still be missing {Resources} and needs manual review; every other organization is unaffected.",
                                command.Slug,
                                orgId,
                                string.Join(", ", addResources));
                        }
                    }
                    else
                    {
                        // Drift: this organization never received a copy of the permission, so
                        // there is no id to bind. Logged rather than failed, because one
                        // organization missing a document must not veto the rest.
                        _logger.LogWarning(
                            "Role permission propagation found no matching permissions to add for role '{Slug}' in organization '{OrganizationId}'. That organization may be missing copies of {Resources}.",
                            command.Slug,
                            orgId,
                            string.Join(", ", addResources));
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
                        var removed = await _resourceRepository.RemoveRolePermissionByIdsAsync(
                            command.Slug,
                            removePermissionIds,orgId);

                        if (!removed)
                        {
                            _logger.LogWarning(
                                "Role permission propagation was not acknowledged when removing permissions from role '{Slug}' in organization '{OrganizationId}'. That organization may still grant {Resources} and needs manual review; every other organization is unaffected.",
                                command.Slug,
                                orgId,
                                string.Join(", ", removeResources));
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Role permission propagation found no matching permissions to remove for role '{Slug}' in organization '{OrganizationId}'. That organization may be missing copies of {Resources}.",
                            command.Slug,
                            orgId,
                            string.Join(", ", removeResources));
                    }
                }
            }

            return true;
        }

        private Task ProcessTimelineAsync(BlocksContext blocksContext, string itemId, string eventname)
        {
            return Task.CompletedTask;
        }

        public async Task<BaseMutationResponse> CreateOrganizationAsync(CreateOrganizationRequest request, string? creatorId = null)
        {
            // Read once and reuse: the AllowOrgCreationFrom* flags below need the object itself.
            // The guard below is what keeps those dereferences safe -- a tenant with no
            // configuration document returns here and never reaches them.
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            if (!IsMultiOrgEnabled(tenantConfig))
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

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = createdByUserId ?? string.Empty,
                Category = UserActivityCategory.Resource,
                Event = "ORGANIZATION_CREATED",
                Source = "iam-organization",
                Entity = "Organization",
                EntityId = organization.ItemId,
                Metadata = new Dictionary<string, string>
                {
                    { "name", organization.Name },
                    { "createdFrom", request.CreatedFrom.ToString() }
                }
            });

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
                    IdpConstants.IamUserQueue,
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


        public async Task DeleteOrganizationAsync(string organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId) || organizationId == DefaultOrganizationId)
            {
                return;
            }

            await _resourceRepository.DeleteOrganizationAsync(organizationId);
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
            if (!await IsMultiOrgEnabledAsync())
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
            if (!await IsMultiOrgEnabledAsync())
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
            if (!await IsMultiOrgEnabledAsync())
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
            if (!await IsMultiOrgEnabledAsync())
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

        private static bool IsDefaultOrgScope(string? organizationId)
        {
            return organizationId == DefaultOrganizationId;
        }

        /// <summary>
        /// Reads tenant configuration and reports whether multi-organization mode is on, treating a
        /// missing configuration document as "off".
        /// </summary>
        /// <remarks>
        /// The configuration is fetched with FirstOrDefaultAsync, so it is null for any tenant that
        /// has never saved one -- a freshly provisioned tenant, or one seeded without it. Callers
        /// must never dereference it directly. On the mutation paths the read happens *after* the
        /// write has already committed, so a NullReferenceException there returns HTTP 500 for an
        /// operation that actually succeeded; the client's retry then trips that operation's own
        /// already-applied guard (Permission_Already_Archived, and so on), leaving no sequence of
        /// calls that ever returns success. Absent configuration means single-organization, which
        /// is also the correct answer: a tenant with no configuration has not enabled multi-org.
        /// </remarks>
        private async Task<bool> IsMultiOrgEnabledAsync([CallerMemberName] string operation = "")
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            return IsMultiOrgEnabled(tenantConfig, operation);
        }

        /// <summary>
        /// Overload for callers that need the configuration object itself afterwards, so it is read
        /// once rather than twice. Same null semantics as <see cref="IsMultiOrgEnabledAsync"/>.
        /// </summary>
        private bool IsMultiOrgEnabled(TenantConfiguration? tenantConfig, [CallerMemberName] string operation = "")
        {
            if (tenantConfig is not null)
            {
                return tenantConfig.IsMultiOrgEnabled;
            }

            _logger.LogWarning(
                "{Operation}: no tenant configuration document exists for this tenant, so multi-organization mode is treated as disabled and cross-organization propagation is skipped. Save the organization configuration to enable it.",
                operation);

            return false;
        }
    }
}
