using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Shared.Serialization;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Iam.DomainService.Resources
{
    public class ResourceMutationService : IResourceMutationService
    {
        private const string DefaultOrganizationId = "default";

        /// <summary>
        /// How deep a role's ancestor chain may go before it is treated as non-terminating.
        /// </summary>
        /// <remarks>
        /// No maximum role depth is defined anywhere in the product, so this is a safety bound
        /// rather than a modelled limit: a visited-set alone stops a cycle, but not a chain that is
        /// merely absurdly long, and each level costs one repository read. Well above any plausible
        /// organizational hierarchy.
        /// </remarks>
        private const int MaxRoleHierarchyDepth = 32;

        /// <summary>
        /// Display name for the built-in "default" organization. Matches the label the
        /// blocks-os organization pickers already show for the same sentinel, so the two
        /// surfaces do not disagree about what it is called.
        /// </summary>
        private const string DefaultOrganizationName = "Default";
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

            _logger.LogInformation("Role creation start");

            // Replaces a flat refusal for any non-default caller. The organization now comes from
            // the signed claim through the shared write-scope rule, which also confirms the
            // organization still exists and is enabled -- a token outlives its organization.
            var (writeOrganizationId, scopeFailure) = await ResolveWriteOrganizationAsync(null);
            if (scopeFailure != null)
            {
                _logger.LogInformation("Role creation end -- Organization Scope Rejected");
                return scopeFailure;
            }

            var organizationId = writeOrganizationId!;

            // A non-default organization only exists when multi-org is on, so a non-default caller
            // in a single-org tenant is a stale token or a misconfiguration, not a request to serve.
            if (!IsDefaultOrgScope(organizationId) && !await IsMultiOrgEnabledAsync())
            {
                _logger.LogInformation("Role creation end -- Multi Org Disabled");
                return Failure("forbidden", "Multi_Org_Required_For_Organization_Role");
            }

            // Name is checked before the slug because it is the thing the administrator typed and
            // recognises; failing on the derived slug first would report a value they never chose.
            if (!string.IsNullOrWhiteSpace(command.Name)
                && await _resourceRepository.HasOwnedRoleWithNameAsync(command.Name, organizationId))
            {
                _logger.LogInformation("Role creation end -- Duplicate Name In Organization");
                return Failure("Name", "Role_Name_Already_Exists_In_Organization");
            }

            // The stored slug is derived here, never taken from the request verbatim, so a caller
            // cannot mint a slug attributed to another organization. Runs before validation so the
            // validator's org-scoped uniqueness check sees the value that will actually be stored.
            if (!string.IsNullOrWhiteSpace(command.Slug))
            {
                var (resolvedSlug, slugError) = await ResolveRoleSlugAsync(command.Slug, organizationId);
                if (slugError != null)
                {
                    _logger.LogInformation("Role creation end -- Slug Unavailable");
                    return Failure("Slug", slugError);
                }

                command.Slug = resolvedSlug!;
            }

            // Advisory, default-organization callers only, and only when the caller has not already
            // acknowledged it. A child organization is never told about a sibling: its own name rule
            // has already decided the request, and a sibling's role inventory is not its business.
            if (IsDefaultOrgScope(organizationId)
                && !command.ConfirmDuplicateName
                && await IsMultiOrgEnabledAsync())
            {
                var duplicateNameAdvisory = await BuildDuplicateNameAdvisoryAsync(command.Name, command.Slug, organizationId);
                if (duplicateNameAdvisory != null)
                {
                    _logger.LogInformation("Role creation end -- Duplicate Name Confirmation Required");
                    return duplicateNameAdvisory;
                }
            }

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
            List<string>? ancestorRoleSlugs = null;
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

                // Resolved before the insert, so a parent that does not exist in the caller's
                // organization or a chain that does not terminate is a validation error rather than
                // a stored role whose hierarchy nobody can walk.
                var (resolved, ancestorError) = await ResolveAncestorRoleSlugsAsync(command.ParentRoleSlug);
                if (ancestorError != null)
                {
                    _logger.LogInformation("Role creation end -- Parent Chain Unresolvable");
                    return Failure("ParentRoleSlug", ancestorError);
                }

                ancestorRoleSlugs = resolved;
            }

            var itemId = await ProcessRoleAsync(command, ancestorRoleSlugs);

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Create,
                    ItemId = itemId,
                    Entity = ResourceEntity.Role
                }
            );

            // Only a default-organization create fans out. An organization-specific role stays
            // where it was made, which is the whole point of it.
            if (IsDefaultOrgScope(organizationId) && await IsMultiOrgEnabledAsync())
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

            // Always the richer type on this endpoint, so a client can read the advisory fields
            // without branching on which shape came back. Zeroes on success.
            return new CreateRoleResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        public async Task<string> ProcessRoleAsync(CreateRoleRequest command, List<string>? ancestorRoleSlugs = null)
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

            // The chain is normally resolved and validated by CreateRoleAsync before this method is
            // reached, so an unresolvable parent is a 400 rather than a half-built role. Recomputed
            // here only if a caller passes nothing, and unresolvable then is a programming error.
            if (!string.IsNullOrWhiteSpace(command.ParentRoleSlug))
            {
                if (ancestorRoleSlugs == null)
                {
                    var (resolved, resolveError) = await ResolveAncestorRoleSlugsAsync(command.ParentRoleSlug);
                    if (resolveError != null)
                    {
                        throw new InvalidOperationException(
                            $"Parent role chain for '{command.ParentRoleSlug}' is not resolvable: {resolveError}");
                    }

                    ancestorRoleSlugs = resolved;
                }

                role.AncestorRoleSlugs = ancestorRoleSlugs ?? [];
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

            // Archiving is ArchivePermissionAsync's job alone. This endpoint used to accept it as a
            // field, which made it a second, silent delete path: one that never refreshed the
            // default organization's role counts and had no way to revoke the direct user grants.
            // Refused rather than ignored so a caller aiming at the wrong endpoint is told, instead
            // of getting a 200 for a delete that did not happen.
            if (command.IsArchived)
            {
                _logger.LogInformation("Permission update end -- Archive Attempted Through Update");
                return Failure("IsArchived", "Use_Delete_Endpoint_To_Archive_Permission");
            }

            // An archived permission is terminal. Editing one would resurrect it below --
            // UpdateAllSamePermissionAsync writes IsArchived to every organization's copy, so a
            // routine name change would revive the permission tenant-wide with all of its role
            // bindings still attached, since the archive deliberately preserves Permission.Roles.
            if (permission.IsArchived)
            {
                _logger.LogInformation("Permission update end -- Already Archived");
                return Failure("archived", "Cannot_Update_Archived_Permission");
            }

            permission.Name = command.Name;
            permission.Description = command.Description;
            permission.Type = command.Type;
            permission.Resource = command.Resource.ToLower();
            permission.LastUpdatedDate = DateTime.Now;
            permission.LastUpdatedBy = blocksContext?.UserId;
            permission.Tags = command.Tags;
            // IsArchived and IsBuiltIn are deliberately NOT taken from the request. Both are
            // lifecycle state rather than editable attributes, and both are non-nullable bools on
            // the request, so a client that omits them sends `false` -- which used to un-archive
            // the permission and strip its built-in flag on an unrelated edit. Clearing IsBuiltIn
            // that way also defeated the root-tenant guard in ArchivePermissionAsync, letting any
            // tenant edit a built-in permission and then archive it.
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

            // Always an update now. The delete variants of both events were only reachable through
            // the archive-by-update path refused above, and the propagation "delete" they queued
            // routed into DeletePermissionForAllOrg -- which skips the default organization on the
            // assumption that ArchivePermissionAsync already handled it, something this method
            // never did.
            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Update,
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

            // Unconditional, unlike the scrub above: this pulls a dangling pointer out of
            // configuration rather than revoking anyone's current access, so there is nothing for a
            // human to consent to. It matters because DefaultPermissionsForNewUserOnSignUp is
            // copied verbatim into User.Permissions for every account created afterwards, and that
            // dictionary is exactly what AuthorizationClaimsResolver reads when minting claims --
            // so a resource left here hands every new signup a working grant on an archived
            // permission, while Signup Configuration goes on listing it as though it existed.
            //
            // Tenant-wide: TenantConfiguration is one document per tenant, so this covers every
            // organization at once and needs no counterpart in DeletePermissionForAllOrg.
            var signUpDefaultsCleaned = await _resourceRepository.RemovePermissionFromSignUpDefaultsAsync(
                permission.Resource);

            if (!signUpDefaultsCleaned)
            {
                _logger.LogWarning(
                    "Permission archive aborted for '{Resource}': the signup-defaults cleanup was not acknowledged. The permission is left active so a retry can complete it, rather than archived while new signups still receive it.",
                    permission.Resource);

                return new BaseMutationResponse();
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

            // Every role that referenced this permission now grants one fewer, and Role.Count is a
            // cache that no longer says so. Only this organization is corrected here: the copies in
            // other organizations are corrected by DeletePermissionForAllOrg, which a
            // single-organization tenant never runs.
            await RefreshRoleCountsAfterArchiveAsync(permission.Roles, permission.OrganizationId);

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

            // The signup defaults are the third reference, and the one that keeps acting after the
            // archive: DefaultRolesForNewUserOnSignUp is copied verbatim onto every account created
            // afterwards, with no archived check anywhere along that path. Left behind, an archived
            // role goes on being written into User.Roles for every new signup -- and goes on being
            // shown in Signup Configuration as though it still existed.
            //
            // Tenant-wide rather than per organization, because TenantConfiguration is a single
            // document per tenant. Not gated on consent: this removes a dangling pointer from
            // configuration, it does not revoke anyone's current access.
            var signUpDefaultsCleaned = await _resourceRepository.RemoveRoleFromSignUpDefaultsAsync(role.Slug);

            if (!permissionsCleaned || !usersCleaned || !signUpDefaultsCleaned)
            {
                _logger.LogWarning(
                    "Role archive aborted for '{Slug}' in organization '{OrganizationId}': reference cleanup was not acknowledged (permissions: {PermissionsCleaned}, users: {UsersCleaned}, signup defaults: {SignUpDefaultsCleaned}). The role is left active so a retry can complete it.",
                    role.Slug,
                    role.OrganizationId,
                    permissionsCleaned,
                    usersCleaned,
                    signUpDefaultsCleaned);

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
                        { "Name", "Maximum_Character_Limit_150" }
                    }
                };
            }

            // The same rule creation enforces, or a rename would walk straight past it. Scoped to
            // the role's own organization and excluding itself, so re-saving a role without
            // changing its name is not a conflict with itself.
            if (await _resourceRepository.HasOwnedRoleWithNameAsync(command.Name, role.OrganizationId, role.ItemId))
            {
                _logger.LogInformation("Role update end -- Duplicate Name In Organization");
                return Failure("Name", "Role_Name_Already_Exists_In_Organization");
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
                var (ancestorRoleSlugs, ancestorError) = await ResolveAncestorRoleSlugsAsync(command.ParentRoleSlug);
                if (ancestorError != null)
                {
                    _logger.LogInformation("Role update end -- Parent Chain Unresolvable");
                    return Failure("ParentRoleSlug", ancestorError);
                }

                role.AncestorRoleSlugs = ancestorRoleSlugs!;
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

            // The token decides, not the payload. Previously this was ResolveOrganizationId, which
            // took command.OrganizationId at face value with no membership test -- so a caller
            // holding mutate-roles in one organization could rebind another organization's role by
            // naming it here.
            var (writeOrganizationId, scopeFailure) = await ResolveWriteOrganizationAsync(command?.OrganizationId);
            if (scopeFailure != null)
            {
                _logger.LogInformation("SetRole end -- Organization Scope Rejected");
                return new SetRolesResponse
                {
                    Errors = scopeFailure.Errors is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string>(scopeFailure.Errors)
                };
            }

            var currentOrganizationId = writeOrganizationId!;
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
                    // Zero, never the source role's Count. Count means "permissions bound to this
                    // slug IN THIS organization", and this copy is created with no bindings at all
                    // -- the binding lives on each organization's own Permission documents, which
                    // this insert does not touch. Copying the source's number would make every
                    // propagated role advertise permissions it does not grant.
                    Count = 0,
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

            // Under consent the direct grants are scrubbed per organization too, so the
            // "no user still holds an archived permission" invariant holds everywhere, not only in
            // the organization the administrator was looking at.
            //
            // Deliberately OUTSIDE the archive loop below, and for the same reason the role-count
            // refresh is: by the time this consumer runs, ArchivePermissionAsync's
            // UpdateAllSamePermissionAsync has usually already flipped IsArchived on every copy --
            // it filters on Resource with no organization clause -- so that loop's IsArchived skip
            // fires for every copy. While this scrub sat inside it, it was unreachable in practice,
            // and every non-default organization kept its direct grants after an archive the
            // administrator had explicitly consented to. Those grants are what mint a token claim,
            // so the permission went on working there indefinitely.
            //
            // Best-effort per organization: the archive has already committed, so an unacknowledged
            // scrub is logged and the rest are still attempted. It can no longer hold back the
            // copy's archive -- that write has usually happened already, so refusing here would
            // protect nothing while skipping the remaining organizations.
            if (confirmRevokeFromUsers)
            {
                foreach (var organizationId in permissions
                    .Select(x => x.OrganizationId)
                    .Where(x => !string.IsNullOrWhiteSpace(x) && x != DefaultOrganizationId)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var usersCleaned = await _resourceRepository.RemovePermissionFromAllUsersAsync(
                        permission.Resource, organizationId);

                    if (!usersCleaned)
                    {
                        _logger.LogWarning(
                            "Permission archive propagation: the direct user-grant cleanup for '{Resource}' in organization '{OrganizationId}' was not acknowledged. Users there may still hold the archived permission and need manual review.",
                            permission.Resource,
                            organizationId);
                    }
                }
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

                orgPermission.IsArchived = true;
                orgPermission.LastUpdatedDate = DateTime.UtcNow;
                orgPermission.LastUpdatedBy = permission.LastUpdatedBy;
                stale++;
            }

            if (stale > 0)
            {
                await _resourceRepository.UpdatePermissionsAsync(permissions);
            }

            // Deliberately outside the loop above and NOT gated on `stale`. By the time this
            // consumer runs, ArchivePermissionAsync's UpdateAllSamePermissionAsync has usually
            // already flipped IsArchived on every copy -- it filters on Resource with no
            // organization clause -- so the loop above skips them and `stale` is often zero while
            // the copies are very much archived. Their roles still advertise a permission that
            // grants nothing, and this is the only place that corrects them.
            //
            // The default organization is skipped because ArchivePermissionAsync already refreshed
            // it, synchronously and regardless of whether this propagation ever runs.
            foreach (var orgPermission in permissions)
            {
                if (orgPermission.OrganizationId == DefaultOrganizationId)
                {
                    continue;
                }

                await RefreshRoleCountsAfterArchiveAsync(orgPermission.Roles, orgPermission.OrganizationId);
            }

            _logger.LogInformation(
                "Archived permission '{Resource}' for {Count} organizations",
                permission.Resource,
                stale);

            return true;
        }

        /// <summary>
        /// Recomputes <see cref="Role.Count"/> for every role that referenced a permission which
        /// has just been archived, in one organization.
        /// </summary>
        /// <remarks>
        /// The slugs come from the archived permission's own Roles array, which the archive leaves
        /// intact -- that array IS the binding, and pulling it would make the soft delete
        /// unrestorable. What changes is that the count query no longer counts an archived
        /// document, so the number has to be recomputed for each role that named it.
        ///
        /// Distinct slugs only: one organization's permission can list the same role twice through
        /// data drift, and recounting it twice is wasted work rather than a wrong answer. Failures
        /// are logged and stepped over, for the same reason the assignment propagation does it --
        /// the archive has already committed, and one stale number must not stop the remaining
        /// roles being corrected.
        /// </remarks>
        private async Task RefreshRoleCountsAfterArchiveAsync(IEnumerable<string>? slugs, string organizationId)
        {
            if (slugs == null)
            {
                return;
            }

            foreach (var slug in slugs
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var counted = await _resourceRepository.UpdateRolesCountAsync(slug, organizationId);

                if (!counted)
                {
                    _logger.LogWarning(
                        "Permission archive: the permission count for role '{Slug}' in organization '{OrganizationId}' was not refreshed. That role still advertises the archived permission until its next change; every other role is unaffected.",
                        slug,
                        organizationId);
                }
            }
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

                // CreatedFromDefault is the third condition, and it is the one that was missing.
                // This lookup matches on slug alone, so an organization that owns a role of its own
                // carrying the same slug -- a private role created before the create guard existed,
                // and so still bare-slugged -- was resolved here and had the default organization's
                // permissions written onto it. The same predicate UpdateRoleForAllOrg and
                // DeleteRoleForAllOrg already use: propagation touches copies, never originals.
                if (orgRole == null || orgRole.IsArchived || !orgRole.CreatedFromDefault)
                {
                    _logger.LogWarning(
                        "Role permission propagation skipped '{Slug}' in organization '{OrganizationId}': the copy is missing, archived, or is that organization's own role. Every other organization is unaffected.",
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

                // Role.Count is a denormalised cache of "permissions bound to this slug in this
                // organization", and it is recomputed from the Permissions collection rather than
                // adjusted by a delta -- so it self-corrects even when one of the writes above was
                // partially applied or the organization had already drifted.
                //
                // ProcessPermissionAsync only ever refreshed the caller's own organization, which
                // left every propagated-to organization advertising a stale count next to bindings
                // that had actually changed. Failures are logged and stepped over for the same
                // reason as the binding writes: a wrong number in one organization must not stop
                // the remaining organizations from being updated at all.
                var counted = await _resourceRepository.UpdateRolesCountAsync(command.Slug, orgId);

                if (!counted)
                {
                    _logger.LogWarning(
                        "Role permission propagation could not refresh the permission count for role '{Slug}' in organization '{OrganizationId}'. The bindings there are correct but the displayed count is stale until the next change to this role; every other organization is unaffected.",
                        command.Slug,
                        orgId);
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
                Attributes = AttributeNormalizer.Normalize(request.Attributes, AttributePolicy.Internal),
            };

            // Branding and localisation are applied through the same guard the update path uses,
            // so a request that omits them keeps the entity's own defaults instead of blanking
            // TimeZone/DateFormat/TimeFormat/Locale to empty strings.
            ApplyProperty(request.Theme, value => organization.Theme = value, v => v != null);
            ApplyProperty(request.LogoUrl, value => organization.LogoUrl = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Industry, value => organization.Industry = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.TimeZone, value => organization.TimeZone = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Currency, value => organization.Currency = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.DateFormat, value => organization.DateFormat = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.TimeFormat, value => organization.TimeFormat = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Locale, value => organization.Locale = value, v => !string.IsNullOrWhiteSpace(v));

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

            // "default" is a scope sentinel, not a document, so there has never been anything to
            // write. Reported on its own rather than falling through to the null lookup below,
            // whose "not found" would read as "wrong id" instead of "not an editable thing".
            if (string.Equals(id, DefaultOrganizationId, StringComparison.Ordinal))
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "default_organization_immutable", "The default organization cannot be updated." }
                    }
                };
            }

            var scope = OrganizationAccessScopeResolver.Resolve(BlocksContext.GetContext()?.OrganizationId);
            if (scope.Kind == OrganizationAccessScopeKind.Denied)
            {
                _logger.LogWarning("Rejected an organization update: the caller's token carries no organization.");
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "organization_scope_denied", "The caller's token carries no organization." }
                    }
                };
            }

            // Reported as "not found", not "forbidden", so the caller learns nothing about whether
            // an organization it may not reach exists. Tested BEFORE the document is read, so an
            // out-of-scope id is indistinguishable from one that does not exist.
            if (!scope.Allows(id))
            {
                _logger.LogWarning(
                    "Rejected an organization update targeting {RequestedOrganizationId}: the caller is scoped to {ScopedOrganizationId}.",
                    id,
                    scope.OrganizationId);

                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "not_found", "Organization not found" }
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

            // These two decide what EVERY future member of the organization is granted on join, and
            // nothing validates the strings, so they are a grant of privilege rather than a
            // description of the organization. An organization-scoped administrator editing its own
            // organization would otherwise be able to name permissions it does not itself hold. Only
            // the tenant-wide caller may set them; for anyone else they are dropped, not rejected, so
            // a client that echoes back the values it just read keeps working.
            if (scope.Kind == OrganizationAccessScopeKind.AllOrganizations)
            {
                ApplyProperty(request.DefaultRoleForMembers, value => organization.DefaultRoleForMembers = value, v => v?.Count > 0);
                ApplyProperty(request.DefaultPermissionsForMembers, value => organization.DefaultPermissionsForMembers = value, v => v?.Count > 0);
            }
            ApplyProperty(request.Email, value => organization.Email = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.PhoneNumber, value => organization.PhoneNumber = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.WebsiteUrl, value => organization.WebsiteUrl = value, v => !string.IsNullOrWhiteSpace(v));
            ApplyProperty(request.Addresses, value => organization.Addresses = value, v => v?.Count > 0);
            // Not routed through ApplyProperty: its guard treats an empty bag as "no change",
            // which left callers with no way to clear attributes once set.
            if (request.Attributes is not null)
            {
                organization.Attributes = AttributeNormalizer.Normalize(request.Attributes, AttributePolicy.Internal);
            }
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

            var scope = OrganizationAccessScopeResolver.Resolve(BlocksContext.GetContext()?.OrganizationId);
            if (scope.Kind == OrganizationAccessScopeKind.Denied)
            {
                _logger.LogWarning("Rejected an organization list: the caller's token carries no organization.");
                return new GetOrganizationsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "organization_scope_denied", "The caller's token carries no organization." }
                    }
                };
            }

            // Here the organization is a filter that narrows a query, not a route id naming a
            // target, so the same discard-rather-than-reject rule UserListOrganizationScope uses
            // applies: a scoped caller's requested ids are replaced by its own organization, and a
            // client asking for one it may not see is answered with what it may see.
            if (scope.Kind == OrganizationAccessScopeKind.Organization)
            {
                request.Filter ??= new GetOrganizationsFilter();
                request.Filter.Ids = [scope.OrganizationId];
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

            var scope = OrganizationAccessScopeResolver.Resolve(BlocksContext.GetContext()?.OrganizationId);
            if (scope.Kind == OrganizationAccessScopeKind.Denied)
            {
                _logger.LogWarning("Rejected an organization read: the caller's token carries no organization.");
                return new GetOrganizationResponse
                {
                    IsSuccess = false,
                    Organization = null,
                    Errors = new Dictionary<string, string>
                    {
                        { "organization_scope_denied", "The caller's token carries no organization." }
                    }
                };
            }

            // Same "not found" as an id that does not exist, and decided before the read, so an
            // out-of-scope organization is not confirmed to exist by the shape of the answer.
            if (!scope.Allows(id))
            {
                _logger.LogWarning(
                    "Rejected an organization read of {RequestedOrganizationId}: the caller is scoped to {ScopedOrganizationId}.",
                    id,
                    scope.OrganizationId);

                return new GetOrganizationResponse
                {
                    IsSuccess = false,
                    Organization = null,
                    Errors = new Dictionary<string, string>
                    {
                        { "not_found", "Organization not found" }
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
                // A disabled organization is not somewhere the member can currently act, so it does
                // not belong in the list a switcher is built from. The assignment picker in blocks-os
                // already filters the same way.
                .Where(x => !x.IsDisabled)
                .Select(x => new MyOrganizationInfo
                {
                    ItemId = x.ItemId,
                    Name = x.Name,
                    CreatedDate = x.CreatedDate
                })
                .OrderBy(x => organizationIds.IndexOf(x.ItemId))
                .ToList();

            // "default" is a scope sentinel, not a row in the organizations collection, so the id
            // lookup above can never resolve it and it silently disappeared from the result -- a
            // user whose only membership is "default" saw an empty list. Re-add it here, but ONLY
            // when the user actually holds that membership: this endpoint reports where the caller
            // belongs, so synthesising it unconditionally would claim a membership they lack.
            if (organizationIds.Contains(DefaultOrganizationId, StringComparer.Ordinal))
            {
                myOrganizations.Insert(0, new MyOrganizationInfo
                {
                    ItemId = DefaultOrganizationId,
                    Name = DefaultOrganizationName,
                    CreatedDate = null
                });
            }

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
        /// Walks a parent role's ancestor chain, innermost first, and returns it or the reason it
        /// could not be resolved.
        /// </summary>
        /// <remarks>
        /// Replaces two copies of a loop that never advanced its cursor: it re-read the same
        /// <c>ParentRoleSlug</c> on every pass, so a parent that itself had a parent appended the
        /// same grandparent forever and the request never returned. It also dereferenced the lookup
        /// result without a null check, so a slug absent from the caller's organization was a
        /// NullReferenceException rather than a validation error.
        /// <para>
        /// The lookup is deliberately the single-argument overload: the repository resolves the
        /// organization from <c>BlocksContext</c>, which is exactly the caller's own organization,
        /// so a parent in another organization is invisible here and reports as not found.
        /// </para>
        /// <para>
        /// Two independent stops. The visited set catches a cycle, which is data that already
        /// exists and would otherwise loop forever. The depth ceiling catches a chain that is
        /// merely pathological rather than circular -- no maximum role depth is defined anywhere,
        /// so without it a long chain would issue one read per level with no bound.
        /// </para>
        /// </remarks>
        private async Task<(List<string>? Ancestors, string? Error)> ResolveAncestorRoleSlugsAsync(string parentRoleSlug)
        {
            var ancestors = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = parentRoleSlug.ToLower();

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (!visited.Add(current) || ancestors.Count >= MaxRoleHierarchyDepth)
                {
                    _logger.LogWarning(
                        "Role hierarchy for parent '{ParentRoleSlug}' does not terminate (stopped at depth {Depth}).",
                        parentRoleSlug,
                        ancestors.Count);

                    return (null, "Role_Hierarchy_Cycle_Detected");
                }

                var parent = await _resourceRepository.GetRoleBySlugAsync(current);
                if (parent == null)
                {
                    return (null, "Parent_Role_Not_Found");
                }

                ancestors.Add(current);
                current = string.IsNullOrWhiteSpace(parent.ParentRoleSlug)
                    ? null
                    : parent.ParentRoleSlug.ToLower();
            }

            return (ancestors, null);
        }

        /// <summary>
        /// Builds the duplicate-name refusal, or null when nothing needs confirming.
        /// </summary>
        /// <remarks>
        /// Counts, never identities: naming the organizations would hand one administrator another
        /// organization's role inventory, and a number is all a confirmation needs.
        /// <para>
        /// The slug-conflict count is the one piece of "will not receive this role" information that
        /// survives, because <c>InsertRoleForAllOrg</c> skips any organization already holding the
        /// slug. It is reported as an exception rather than as a per-organization breakdown.
        /// </para>
        /// <para>
        /// A failed count refuses rather than proceeding. The whole point of the advisory is that
        /// the same-name pair is not created unnoticed, so an unavailable count must not be read as
        /// "no collision".
        /// </para>
        /// </remarks>
        private async Task<CreateRoleResponse?> BuildDuplicateNameAdvisoryAsync(
            string name, string slug, string organizationId)
        {
            List<Role> others;
            try
            {
                others = await _resourceRepository.GetOwnedRolesWithNameInOtherOrganizationsAsync(name, organizationId)
                    ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Could not count organizations already using the role name '{Name}'. Refusing the create rather than risking an unnoticed duplicate; the caller may confirm to proceed.",
                    name);

                return new CreateRoleResponse
                {
                    IsSuccess = false,
                    RequiresDuplicateNameConfirmation = true,
                    Errors = new Dictionary<string, string>
                    {
                        { "duplicate_name", "Role_Name_Exists_In_Other_Organizations" }
                    }
                };
            }

            if (others.Count == 0)
            {
                return null;
            }

            var duplicateNameOrganizations = others
                .Select(x => x.OrganizationId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (duplicateNameOrganizations.Count == 0)
            {
                return null;
            }

            var slugConflictOrganizations = others
                .Where(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.OrganizationId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return new CreateRoleResponse
            {
                IsSuccess = false,
                RequiresDuplicateNameConfirmation = true,
                DuplicateNameOrganizationCount = duplicateNameOrganizations.Count,
                SlugConflictOrganizationCount = slugConflictOrganizations,
                Errors = new Dictionary<string, string>
                {
                    { "duplicate_name", "Role_Name_Exists_In_Other_Organizations" }
                }
            };
        }

        /// <summary>
        /// Derives the slug a new role will actually be stored under, and confirms it is free.
        /// </summary>
        /// <remarks>
        /// A default-organization role keeps the submitted base unchanged, because propagation and
        /// every existing consumer match default roles by their bare slug. An organization-specific
        /// role gets the base plus an underscore and a fragment of the organization id, which makes
        /// a cross-organization slug collision structurally impossible: default propagation can no
        /// longer be shadowed by a private role, and a private role can no longer be reached by a
        /// slug-matching lookup meant for copies.
        /// <para>
        /// The fragment starts at 8 hexadecimal characters -- a full GUID would add 37 characters to
        /// a value that travels in role claims -- and lengthens only when ANOTHER organization
        /// already holds the candidate, which is the birthday collision between two organization
        /// ids sharing a prefix. A collision inside the caller's OWN organization is a genuine
        /// duplicate and is reported, never worked around: the caller chose the base and can choose
        /// another.
        /// </para>
        /// <para>
        /// The lookup is the archive-inclusive one on purpose. A slug is the binding held in
        /// <c>User.Roles[orgId]</c> and <c>Permission.Roles</c>, so an archived role keeps
        /// reserving it -- and because that role is hidden from every list, the error has to say so
        /// or it reads as a phantom conflict.
        /// </para>
        /// </remarks>
        private async Task<(string? Slug, string? Error)> ResolveRoleSlugAsync(string baseSlug, string organizationId)
        {
            var normalizedBase = baseSlug.Trim().ToLower();

            if (IsDefaultOrgScope(organizationId))
            {
                var takenBy = await _resourceRepository.GetRolesBySlugAsync(normalizedBase);
                return takenBy is { Count: > 0 }
                    ? (null, "Role_Slug_Already_In_Use_Including_Archived_Roles")
                    : (normalizedBase, null);
            }

            var fragmentSource = organizationId.Replace("-", string.Empty, StringComparison.Ordinal).ToLower();

            for (var length = 8; length <= fragmentSource.Length; length += 4)
            {
                var candidate = $"{normalizedBase}_{fragmentSource[..Math.Min(length, fragmentSource.Length)]}";
                var holders = await _resourceRepository.GetRolesBySlugAsync(candidate);

                if (holders == null || holders.Count == 0)
                {
                    return (candidate, null);
                }

                if (holders.Any(x => string.Equals(x.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase)))
                {
                    return (null, "Role_Slug_Already_In_Use_Including_Archived_Roles");
                }

                _logger.LogWarning(
                    "Role slug '{Candidate}' is already held by another organization; lengthening the organization fragment.",
                    candidate);
            }

            return (null, "Role_Slug_Already_In_Use_Including_Archived_Roles");
        }

        /// <summary>
        /// Resolves which organization a role or permission write may target, and confirms that
        /// organization is one the caller can still act in.
        /// </summary>
        /// <remarks>
        /// Two separate questions, answered in order. <see cref="ResourceWriteOrganizationScope"/>
        /// decides scope from the signed claim and discards any organization named in the payload
        /// unless the caller is tenant-wide, so a request body can never widen what a token
        /// authorises. Then the organization is confirmed to exist and be enabled, because an
        /// access token outlives its organization: <c>DeleteOrganizationAsync</c> hard-deletes the
        /// document and <c>IsDisabled</c> flips through update, while the claim stays valid until
        /// expiry. <c>GetMyOrganizations</c> already hides disabled organizations on the grounds
        /// that they are not somewhere a member can act; writes agree with it here.
        /// <para>
        /// "default" is exempt from the existence lookup: it is a scope sentinel, not a row in the
        /// organizations collection, so looking it up would fail every tenant-wide write.
        /// </para>
        /// <para>
        /// A lookup that throws is left to propagate rather than being swallowed into a "valid"
        /// answer -- failing closed is the point of the check.
        /// </para>
        /// </remarks>
        private async Task<(string? OrganizationId, BaseMutationResponse? Failure)> ResolveWriteOrganizationAsync(
            string? requestedOrganizationId)
        {
            var scope = ResourceWriteOrganizationScope.Resolve(
                BlocksContext.GetContext()?.OrganizationId,
                requestedOrganizationId);

            if (scope.Kind == ResourceWriteScopeKind.Denied)
            {
                _logger.LogWarning("Rejected a role/permission write: the caller's token carries no organization.");
                return (null, Failure("unauthorized", "Organization_Not_Resolved"));
            }

            if (IsDefaultOrgScope(scope.OrganizationId))
            {
                return (scope.OrganizationId, null);
            }

            var organization = await _resourceRepository.GetOrganizationById(scope.OrganizationId);
            if (organization == null)
            {
                _logger.LogWarning(
                    "Rejected a role/permission write: organization '{OrganizationId}' has no document.",
                    scope.OrganizationId);

                return (null, Failure("forbidden", "Organization_Not_Found"));
            }

            if (organization.IsDisabled)
            {
                _logger.LogWarning(
                    "Rejected a role/permission write: organization '{OrganizationId}' is disabled.",
                    scope.OrganizationId);

                return (null, Failure("forbidden", "Organization_Disabled"));
            }

            return (scope.OrganizationId, null);
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
