using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Shared.Serialization;
using Iam.DomainService.Utilities;
using Iam.DomainService.Users.RequestModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Iam.DomainService.Users
{
    public class UserManagementMutationService : IUserManagementMutationService
    {
        private const string DefaultOrganizationId = "default";
        private const int MaxPermissionsPerUser = 5;
        private readonly ILogger<UserManagementMutationService> _logger;
        private readonly IValidator<CreateUserRequest> _createValidator;
        private readonly IValidator<UpdateUserRequest> _updateValidator;
        private readonly IValidator<UpdateMyAccountRequest> _myAccountValidator;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly IUserRepository _userRepository;
        private readonly IIdentityAccessManagementRepository? _identityAccessManagementRepository;
        private readonly IResourceRepository? _resourceRepository;
        private readonly IMessageClient _messageClient;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly IDefaultOidcClientResolver? _defaultOidcClientResolver;
        private readonly IConfiguration? _configuration;
        public UserManagementMutationService(
            ILogger<UserManagementMutationService> logger,
            IValidator<CreateUserRequest> createValidator,
            IValidator<UpdateUserRequest> updateValidator,
            IValidator<UpdateMyAccountRequest> myAccountValidator,
            IIdentityAccessManagementService identityAccessManagementService,
            IUserRepository userRepository,
            IMessageClient messageClient,
            ICacheClient cacheClient,
            ITenants tenants,
            IUserActivityDispatcher userActivityDispatcher,
            IIdentityAccessManagementRepository? identityAccessManagementRepository = null,
            IResourceRepository? resourceRepository = null,
            IHttpContextAccessor? httpContextAccessor = null,
            IDefaultOidcClientResolver? defaultOidcClientResolver = null,
            IConfiguration? configuration = null
        )
        {
            _logger = logger;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _myAccountValidator = myAccountValidator;
            _identityAccessManagementService = identityAccessManagementService;
            _userRepository = userRepository;
            _messageClient = messageClient;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _userActivityDispatcher = userActivityDispatcher;
            _identityAccessManagementRepository = identityAccessManagementRepository;
            _resourceRepository = resourceRepository;
            _httpContextAccessor = httpContextAccessor;
            _defaultOidcClientResolver = defaultOidcClientResolver;
            _configuration = configuration;
        }

        public async Task<BaseMutationResponse> CreateUserAsync(CreateUserRequest command)
        {
            _logger.LogInformation("User creation start");

            var validationResult = await _createValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("User creation end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            // Resolved before the existence check because both the caller guard and the merge below
            // need the normalized organization and its member defaults.
            await ApplyOrganizationDefaultsAsync(command);
            var organizationId = command.OrganizationId ?? DefaultOrganizationId;

            var organizationGuardFailure = ValidateCallerMayWriteToOrganization(organizationId);
            if (organizationGuardFailure != null)
            {
                _logger.LogInformation("User creation end -- Organization Guard Error");
                return organizationGuardFailure;
            }

            var permissionCountFailure = ValidatePermissionCount(command.Permissions);
            if (permissionCountFailure != null)
            {
                _logger.LogInformation("User creation end -- Permission Count Error");
                return permissionCountFailure;
            }

            var existingUser = await TryGrantOrganizationToExistingUserAsync(
                command.Email,
                organizationId,
                command.Roles,
                command.Permissions);

            if (existingUser != null)
            {
                _logger.LogInformation(
                    "User creation end -- email already has an account; granted organization {OrganizationId} instead of creating a duplicate",
                    organizationId);

                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = existingUser.ItemId
                };
            }

            string itemId;
            try
            {
                itemId = await ProcessCreateUserAsync(command);
            }
            catch (ValidationException ex)
            {
                _logger.LogInformation("User creation end -- Signup Policy Validation Error");
                return new BaseMutationResponse
                {
                    Errors = ex.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            await SendEvent(itemId, MutationEventType.Create, command.ClientId, command.RedirectUri);
            var bc = BlocksContext.GetContext();

            _logger.LogInformation("User creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        private async Task SendEvent(
            string itemId,
            MutationEventType mutationEventType,
            string? clientId = null,
            string? redirectUri = null)
        {
            _logger.LogInformation("User mutation event -- initiate");
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<UserMutationEvent>
                {
                    ConsumerName = IdpConstants.IamUserQueue,
                    Payload = new UserMutationEvent
                    {
                        ItemId = itemId,
                        Action = mutationEventType,
                        // The email is built by the consumer, which only loads the User —
                        // so the originating app's OIDC context has to travel on the message.
                        ClientId = clientId,
                        RedirectUri = redirectUri
                    }
                }
            );
            _logger.LogInformation("User mutation event -- sent");
        }

        /// <summary>
        /// Pins the command to a concrete organization and fills in that organization's member
        /// defaults. Shared by the create path and the grant-to-existing-user path so a user ends
        /// up with the same roles whichever way they were added. Idempotent: running it twice on
        /// the same command changes nothing.
        /// </summary>
        private async Task ApplyOrganizationDefaultsAsync(CreateUserRequest command)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            command.OrganizationId = tenantConfig?.IsMultiOrgEnabled ?? false
            ? string.IsNullOrWhiteSpace(command.OrganizationId) ? DefaultOrganizationId : command.OrganizationId
            : DefaultOrganizationId;

            var organization = null as Organization;
            if (command.OrganizationId != DefaultOrganizationId)
            {
                organization = await _resourceRepository.GetOrganizationById(command.OrganizationId);
            }

            if(command.Roles == null || command.Roles.Count == 0)
            {
                command.Roles = organization != null && organization.DefaultRoleForMembers != null && organization.DefaultRoleForMembers.Count > 0
                    ? organization.DefaultRoleForMembers
                    : [];
            }

            command.Permissions ??= organization != null && organization.DefaultPermissionsForMembers != null && organization.DefaultPermissionsForMembers.Count > 0
                    ? organization.DefaultPermissionsForMembers
                    : [];
        }

        /// <summary>
        /// The single place every creation path asks "does this email already have an account?".
        /// <para>
        /// The lookup is deliberately tenant-wide rather than organization-scoped. <c>User.OrganizationIds</c>
        /// is a list and <c>Roles</c> / <c>Permissions</c> are dictionaries keyed by organization, so one
        /// account is meant to span organizations. An organization-scoped lookup answers "no such user"
        /// for an account that exists in a different organization -- and answers it for *every*
        /// organization when <c>OrganizationIds</c> is empty -- which is what produced two documents
        /// for one email.
        /// </para>
        /// <para>
        /// Returns the existing account with the requested organization granted, or <c>null</c> when the
        /// email is new and the caller should go on to create one.
        /// </para>
        /// <para>
        /// Account lifecycle is deliberately not consulted here. There is no way to disable an account
        /// yet, and when that feature arrives the intended answer is to reactivate on invite rather
        /// than refuse -- so the decision belongs with that feature, not with this lookup.
        /// </para>
        /// </summary>
        private async Task<User?> TryGrantOrganizationToExistingUserAsync(
            string email,
            string organizationId,
            List<string>? roles,
            List<string>? permissions)
        {
            var existingUser = await _userRepository.GetUserByEmailAsync(email);
            if (existingUser == null)
            {
                return null;
            }

            GrantOrganization(existingUser, organizationId, roles, permissions);

            existingUser.LastUpdatedDate = DateTime.UtcNow;
            existingUser.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? existingUser.ItemId;

            await _userRepository.UpdateUserAsync(existingUser);

            return existingUser;
        }

        /// <summary>
        /// Places <paramref name="user"/> in <paramref name="organizationId"/> and sets that
        /// organization's roles and permissions.
        /// <para>
        /// Membership is read the same three ways <c>OrganizationAccessResolver.HasOrganizationAccess</c>
        /// reads it -- an organization can have been granted through a <c>Roles</c> or <c>Permissions</c>
        /// key without ever reaching <c>OrganizationIds</c>. Testing <c>OrganizationIds</c> alone would
        /// read such a member as a new joiner and overwrite the roles they already hold with an empty
        /// list. Falling back to what is already stored means an empty request never clears anything,
        /// while <c>OrganizationIds</c> is brought back in step with the other two.
        /// </para>
        /// </summary>
        private static void GrantOrganization(User user, string organizationId, List<string>? roles, List<string>? permissions)
        {
            if (!user.OrganizationIds.Contains(organizationId))
            {
                user.OrganizationIds.Add(organizationId);
            }

            user.Roles[organizationId] = roles?.Count > 0
                ? roles
                : user.Roles.GetValueOrDefault(organizationId, []);

            user.Permissions[organizationId] = permissions?.Count > 0
                ? permissions
                : user.Permissions.GetValueOrDefault(organizationId, []);
        }

        /// <summary>
        /// The per-organization permission cap, as a check the grant paths can run before writing.
        /// <c>ProcessCreateUserAsync</c> enforces the same rule for a brand new account by throwing;
        /// granting an organization to an existing account has to enforce it too, or an invite
        /// becomes a way around the cap.
        /// </summary>
        private static BaseMutationResponse? ValidatePermissionCount(List<string>? permissions)
        {
            if (permissions is not { Count: > MaxPermissionsPerUser })
            {
                return null;
            }

            return new BaseMutationResponse
            {
                Errors = new Dictionary<string, string>
                {
                    { nameof(CreateUserRequest.Permissions), $"A maximum of {MaxPermissionsPerUser} permissions can be added with any user permission." }
                }
            };
        }

        /// <summary>
        /// Mirrors the guard in <see cref="UpdateUserAccessControlAsync"/>: an organization-scoped
        /// caller may only place a user in its own organization, and only the tenant-wide "default"
        /// context may name any organization. Needed here because the create paths now grant access
        /// to an *existing* account, so an unchecked <c>OrganizationId</c> from a request body would
        /// let one organization's admin hand out roles inside another's.
        /// <para>
        /// A caller carrying no organization at all -- anonymous signup, queue consumers, the SSO
        /// consent exchange -- is left alone: there is no caller organization to compare against, and
        /// those paths do not take the organization from a browser-supplied body.
        /// </para>
        /// </summary>
        private static BaseMutationResponse? ValidateCallerMayWriteToOrganization(string? organizationId)
        {
            var context = BlocksContext.GetContext();

            // The exemption is keyed on having no identity at all -- anonymous signup, queue
            // consumers, the SSO consent exchange -- not on a blank organization. An *authenticated*
            // caller whose token carries no organization, or carries the "no-org" sentinel, is
            // refused like any other, so a token cannot buy tenant-wide reach by omitting the claim.
            if (context is null || !context.IsAuthenticated)
            {
                return null;
            }

            var callerOrganizationId = context.OrganizationId;

            if (string.Equals(callerOrganizationId, DefaultOrganizationId, StringComparison.Ordinal)
                || string.Equals(callerOrganizationId, organizationId, StringComparison.Ordinal))
            {
                return null;
            }

            return new BaseMutationResponse
            {
                Errors = new Dictionary<string, string>
                {
                    { nameof(CreateUserRequest.OrganizationId), "Other org user can not add/update" }
                }
            };
        }

        public async Task<string> ProcessCreateUserAsync(CreateUserRequest command)
        {
            await ApplyOrganizationDefaultsAsync(command);

            if (command.Permissions != null && command.Permissions.Count > MaxPermissionsPerUser)
            {
                throw new ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(command.Permissions),
                        $"A maximum of {MaxPermissionsPerUser} permissions can be added with any user permission.")
                });
            }

            var user = MapUser(command);
            await _userRepository.CreateUserAsync(user);

            return user.ItemId;
        }

        public User MapUser(CreateUserRequest command)
        {
            var id = string.IsNullOrWhiteSpace(command.UserId) ? Guid.NewGuid().ToString() : command.UserId;
            var bc = BlocksContext.GetContext();
            var tenantId = bc?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;

            var normalizedEmail = NormalizeEmail(command.Email);
            var normalizedUserName = NormalizeIdentity(string.IsNullOrWhiteSpace(command.UserName) ? command.Email : command.UserName);

            var user = new User
            {
                ItemId = id,
                CreatedDate = DateTime.Now,
                CreatedBy = bc?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = bc?.UserId ?? id,
                Email = normalizedEmail,
                UserName = normalizedUserName,
                Password = string.IsNullOrWhiteSpace(command.Password) ? string.Empty : _identityAccessManagementService.HashPassword(command.Password, tenant?.TenantSalt),
                PasswordSetTime = string.IsNullOrWhiteSpace(command.Password) ? DateTime.MinValue : DateTime.Now,
                PasswordChangedAtUtc = string.IsNullOrWhiteSpace(command.Password) ? null : DateTime.UtcNow,
                LastCredentialRotationAtUtc = string.IsNullOrWhiteSpace(command.Password) ? null : DateTime.UtcNow,
                PhoneNumber = command.PhoneNumber ?? string.Empty,
                Language = command.Language ?? "en-US",
                Salutation = command.Salutation ?? string.Empty,
                FirstName = command.FirstName ?? string.Empty,
                LastName = command.LastName ?? string.Empty,
                Platform = command.Platform,
                UserCreationType = command.UserCreationType,
                UserPassType = command.UserPassType,
                Tags = command.Tags ?? new List<string>(),
                VerifiedType = command.VerifiedType,
                ProfileImageUrl = command.ProfileImageUrl,
                ProfileImageId = command.ProfileImageId,
                AllowedLogInType = command.AllowedLogInType,
                MfaEnabled = command.MfaEnabled,
                UserMfaType = command.UserMfaType,
                MfaMethods = new List<UserMfaEnrollment>(),
                MailPurpose = string.IsNullOrWhiteSpace(command.MailPurpose) ? "AccountActivation" : command.MailPurpose,
                ProvisioningSource = ResolveProvisioningSource(command.UserCreationType),
                Status = command.VerifiedType == UserVerifiedType.None ? UserLifecycleStatus.PendingVerification : UserLifecycleStatus.Active,
                EmailVerifiedAtUtc = command.VerifiedType == UserVerifiedType.Email ? DateTime.UtcNow : null,
                PhoneVerifiedAtUtc = command.VerifiedType is UserVerifiedType.Sms or UserVerifiedType.WhatsApp ? DateTime.UtcNow : null,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                TokenVersion = 1,
                FailedLoginCount = 0,
                LastFailedLoginUtc = null,
                LockoutUntilUtc = null,
                TermsAcceptedAtUtc = null,
                PrivacyAcceptedAtUtc = null,
                ExternalIdentities = new List<ExternalIdentity>(),
                Attributes = AttributeNormalizer.Normalize(command.Attributes, AttributePolicy.Internal),
            };

            if (!string.IsNullOrWhiteSpace(command.OrganizationId) && !user.OrganizationIds.Contains(command.OrganizationId)) 
                 user.OrganizationIds.Add(command.OrganizationId) ;

            user.Roles = !user.Roles.ContainsKey(command.OrganizationId)?  new Dictionary<string, List<string>> { [command.OrganizationId] = command.Roles ?? [] }: user.Roles;
            user.Permissions = !user.Permissions.ContainsKey(command.OrganizationId)?  new Dictionary<string, List<string>> { [command.OrganizationId] = command.Permissions ?? [] }: user.Permissions;

            return user;
        }

        public async Task<BaseMutationResponse> UpdateUserAsync(UpdateUserRequest command)
        {
            _logger.LogInformation("User update start");

            var validationResult = _updateValidator.Validate(command);

            if (!validationResult.IsValid)
            {
                _logger.LogInformation("User update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);
            if (user == null)
            {
                _logger.LogInformation("User update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Not found" }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();
            var organizationId = !string.IsNullOrWhiteSpace(command.OrganizationId) ? command.OrganizationId: blocksContext.OrganizationId;

            if (organizationId == null && (blocksContext?.OrganizationId == null || blocksContext?.OrganizationId == DefaultOrganizationId))
            {
                organizationId = DefaultOrganizationId;
            }

            if (organizationId == null)
            {
                _logger.LogInformation("User update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "OrganizationId", "User does not belong to the organization in context" }
                    }
                };
            }

            WarnOnRetiredFields(command.UnmappedFields, command.ItemId);

            ApplyProfileFields(user, new ProfilePatch(
                command.Salutation, command.FirstName, command.LastName, command.PhoneNumber,
                command.Language, command.ProfileImageUrl, command.ProfileImageId,
                command.Tags, command.Attributes));

            user.LastUpdatedDate = DateTime.UtcNow;
            user.LastUpdatedBy = blocksContext?.UserId ?? user.ItemId;

            // Membership is not a side effect of editing a profile. Adding the context
            // organization here enrolled users as a by-product of a name change;
            // UpdateUserAccessControlAsync owns OrganizationIds.

            var result = await _userRepository.UpdateUserAsync(user);

            if (!result)
            {
                _logger.LogInformation("User update end -- Error");
                return new BaseMutationResponse();
            }

            _logger.LogInformation("User update end -- Success");

            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        /// <summary>
        /// Sparse update of the caller's own profile. Same apply routine as
        /// <see cref="UpdateUserAsync"/>, so the guard semantics cannot drift between the admin and
        /// self-service surfaces; the subject is taken from the context rather than the payload.
        /// </summary>
        public async Task<BaseMutationResponse> UpdateMyAccountAsync(UpdateMyAccountRequest command)
        {
            ArgumentNullException.ThrowIfNull(command);

            _logger.LogInformation("My account update start");

            var blocksContext = BlocksContext.GetContext();
            var userId = blocksContext?.UserId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogInformation("My account update end -- No authenticated user");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string> { { "ItemId", "Not found" } }
                };
            }

            var validationResult = _myAccountValidator.Validate(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("My account update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogInformation("My account update end -- Not found");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string> { { "ItemId", "Not found" } }
                };
            }

            WarnOnRetiredFields(command.UnmappedFields, userId);

            ApplyProfileFields(user, new ProfilePatch(
                command.Salutation, command.FirstName, command.LastName, command.PhoneNumber,
                command.Language, command.ProfileImageUrl, command.ProfileImageId,
                Tags: null, command.Attributes));

            user.LastUpdatedDate = DateTime.UtcNow;
            user.LastUpdatedBy = userId;

            if (!await _userRepository.UpdateUserAsync(user))
            {
                _logger.LogInformation("My account update end -- Error");
                return new BaseMutationResponse();
            }

            _logger.LogInformation("My account update end -- Success");

            return new BaseMutationResponse { IsSuccess = true, ItemId = user.ItemId };
        }

        /// <summary>
        /// One field of a sparse profile update. <c>null</c> means the caller did not mention the
        /// field; an empty string, list or dictionary means they asked for it to be cleared.
        /// </summary>
        private sealed record ProfilePatch(
            string? Salutation,
            string? FirstName,
            string? LastName,
            string? PhoneNumber,
            string? Language,
            string? ProfileImageUrl,
            string? ProfileImageId,
            List<string>? Tags,
            Dictionary<string, object>? Attributes);

        /// <summary>
        /// The single place the sparse contract is enforced. Every line is a null guard rather than
        /// a <c>?? string.Empty</c>: the coalescing form wrote an empty string over a field the
        /// caller never mentioned, which is what made an omission destructive.
        /// </summary>
        private static void ApplyProfileFields(User user, ProfilePatch patch)
        {
            if (patch.Salutation is not null) user.Salutation = patch.Salutation;
            if (patch.FirstName is not null) user.FirstName = patch.FirstName;
            if (patch.LastName is not null) user.LastName = patch.LastName;
            if (patch.PhoneNumber is not null) user.PhoneNumber = patch.PhoneNumber;
            if (patch.Language is not null) user.Language = patch.Language;
            if (patch.ProfileImageUrl is not null) user.ProfileImageUrl = patch.ProfileImageUrl;
            if (patch.ProfileImageId is not null) user.ProfileImageId = patch.ProfileImageId;
            if (patch.Tags is not null) user.Tags = patch.Tags;

            if (patch.Attributes is not null)
            {
                user.Attributes = AttributeNormalizer.Normalize(patch.Attributes, AttributePolicy.Internal);
            }
        }

        /// <summary>
        /// Roles, permissions and MFA state left this endpoint for the services that own them. The
        /// binder skips unmatched properties, so a caller still sending them would get a 200 and no
        /// change at all - this turns that silence into a log line.
        /// </summary>
        private void WarnOnRetiredFields(Dictionary<string, JsonElement>? unmapped, string? userId)
        {
            if (unmapped is null || unmapped.Count == 0)
            {
                return;
            }

            var retired = unmapped.Keys
                .Where(key => RetiredUpdateFields.Contains(key))
                .ToList();

            if (retired.Count > 0)
            {
                _logger.LogWarning(
                    "User update for {UserId} carried retired field(s) {Fields}; ignored. Roles and permissions belong to POST users/access, MFA state to the MFA endpoints.",
                    userId, string.Join(", ", retired));
            }
        }

        private static readonly HashSet<string> RetiredUpdateFields =
            new(StringComparer.OrdinalIgnoreCase) { "roles", "permissions", "mfaEnabled", "userMfaType", "itemId", "organizationId" };

        public async Task<BaseResponse> DeactivateUserAsync(DeactivateUserRequest request)
        {
            var user = await _userRepository.GetUserByIdAsync(request.UserId);
            if (user == null)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "user_not_found", $"No user found with id {request.UserId}" } } };
            }

            user.Active = false;
            user.Status = UserLifecycleStatus.Disabled;
            user.StatusReason = "deactivated";
            user.DeactivatedAtUtc = DateTime.UtcNow;
            user.DeactivatedBy = BlocksContext.GetContext()?.UserId ?? request.UserId;
            user.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? request.UserId;
            user.LastUpdatedDate = DateTime.Now;

            await Task.WhenAll(
            _userRepository.UpdateUserAsync(user),
            _messageClient.SendToConsumerAsync(new ConsumerMessage<UserStatusChangedEvent>
            {
                ConsumerName = IdpConstants.IamUserQueue,
                Payload = new UserStatusChangedEvent
                {
                    UserId = request.UserId,
                    IsActive = false
                }
            }));
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseMutationResponse> ActivateUserAsync(ActivateUserByAdminRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.UserId), "UserId is required" }
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 3)
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.Reason), "A reason is required (minimum 3 characters)" }
                    }
                };
            }

            var user = await _userRepository.GetUserByIdAsync(request.UserId);
            if (user == null)
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.UserId), $"No user found with id {request.UserId}" }
                    }
                };
            }

            if (user.Active && user.Status == UserLifecycleStatus.Active)
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.UserId), "User is already active" }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();
            var actorUserId = blocksContext?.UserId ?? request.UserId;

            user.Active = true;
            user.IsVerified = true;
            user.Status = UserLifecycleStatus.Active;
            user.StatusReason = "activated";
            user.ActivatedAtUtc = DateTime.UtcNow;
            user.ActivatedBy = actorUserId;
            user.DeactivatedAtUtc = null;
            user.DeactivatedBy = null;
            user.LockoutUntilUtc = null;
            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.LastUpdatedBy = actorUserId;
            user.LastUpdatedDate = DateTime.Now;

            await Task.WhenAll(
                _userRepository.UpdateUserAsync(user),
                _messageClient.SendToConsumerAsync(new ConsumerMessage<UserStatusChangedEvent>
                {
                    ConsumerName = IdpConstants.IamUserQueue,
                    Payload = new UserStatusChangedEvent
                    {
                        UserId = request.UserId,
                        IsActive = true
                    }
                }));

            await SendEvent(user.ItemId, MutationEventType.Update);

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = user.ItemId,
                ActorUserId = actorUserId,
                Category = UserActivityCategory.Account,
                Event = "USER_ACTIVATED",
                Source = "iam-user-mutation",
                Entity = "User",
                EntityId = user.ItemId,
                ReasonCode = "ADMIN_REINSTATE",
                Metadata = new Dictionary<string, string>
                {
                    { "reason", request.Reason }
                }
            });

            _logger.LogInformation("User activation end -- Success for {Id}", user.ItemId);
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        public async Task<BaseMutationResponse> ActivateAndLinkSocialIdentityAsync(ActivateAndLinkSocialIdentityRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.UserId), "UserId is required" }
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(request.Provider))
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.Provider), "Provider is required" }
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(request.ProviderUserId))
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.ProviderUserId), "ProviderUserId is required" }
                    }
                };
            }

            var user = await _userRepository.GetUserByIdAsync(request.UserId);
            if (user == null)
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(request.UserId), $"No user found with id {request.UserId}" }
                    }
                };
            }

            if (user.Active && user.IsVerified)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = user.ItemId
                };
            }

            if (user.Status == UserLifecycleStatus.Disabled)
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "account_disabled", "This account has been disabled by an administrator. Please contact support." }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();
            var actorUserId = blocksContext?.UserId ?? request.UserId;

            user.Active = true;
            user.IsVerified = true;
            user.Status = UserLifecycleStatus.Active;
            user.StatusReason = "activated_via_social_link";
            user.ActivatedAtUtc = DateTime.UtcNow;
            user.ActivatedBy = actorUserId;
            user.EmailVerifiedAtUtc = DateTime.UtcNow;
            user.VerifiedType = user.VerifiedType == UserVerifiedType.None
                ? UserVerifiedType.Email
                : user.VerifiedType;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;
            user.LastUpdatedBy = actorUserId;
            user.LastUpdatedDate = DateTime.Now;

            if (user.ExternalIdentities == null)
            {
                user.ExternalIdentities = new List<ExternalIdentity>();
            }

            var alreadyLinked = user.ExternalIdentities.Any(ei =>
                string.Equals(ei.Provider, request.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ei.ProviderUserId, request.ProviderUserId, StringComparison.OrdinalIgnoreCase));

            if (!alreadyLinked)
            {
                user.ExternalIdentities.Add(new ExternalIdentity
                {
                    Provider = request.Provider,
                    ProviderUserId = request.ProviderUserId,
                    Issuer = request.Issuer ?? request.Provider,
                    LinkedAtUtc = DateTime.UtcNow
                });
            }

            var updated = await _userRepository.UpdateUserAsync(user);
            if (!updated)
            {
                _logger.LogError("ActivateAndLinkSocialIdentityAsync -- Repository update failed for user {Id}", user.ItemId);
                return new BaseMutationResponse();
            }

            await Task.WhenAll(
                _messageClient.SendToConsumerAsync(new ConsumerMessage<UserStatusChangedEvent>
                {
                    ConsumerName = IdpConstants.IamUserQueue,
                    Payload = new UserStatusChangedEvent
                    {
                        UserId = user.ItemId,
                        IsActive = true
                    }
                }));

            await SendEvent(user.ItemId, MutationEventType.Update);

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = user.ItemId,
                ActorUserId = actorUserId,
                Category = UserActivityCategory.Account,
                Event = "USER_ACTIVATED_VIA_SOCIAL_LINK",
                Source = "iam-user-mutation",
                Entity = "User",
                EntityId = user.ItemId,
                ReasonCode = "SOCIAL_SIGNUP_RECOVERY",
                Metadata = new Dictionary<string, string>
                {
                    { "provider", request.Provider },
                    { "issuer", request.Issuer ?? request.Provider }
                }
            });

            _logger.LogInformation("User activation via social link -- Success for {Id} provider={Provider}", user.ItemId, request.Provider);
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        public async Task ExecuteUserMutationCommandAsync(UserMutationEvent command)
        {
            _logger.LogInformation("User Mutation event -- initiate");

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);

            await SendActivationAsync(user, command.ClientId, command.RedirectUri);
            await PublishUserActivityAsync(user, command.Action);
        }

        private async Task<bool> SendActivationAsync(User user, string? clientId = null, string? redirectUri = null)
        {
            _logger.LogInformation("Send Activation for {Id}", user.ItemId);
            var config = await _userRepository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var bc = BlocksContext.GetContext();
            var path = $"{(config.IsOidcEnabled ? IdpConstants.OidcActivateRoute + bc.TenantId : config.AccountActivationPath)}?code={key}&lang={user.Language}";

            // Only the OIDC flow has a client to return to; the legacy activation path is
            // left exactly as it was.
            if (config.IsOidcEnabled)
            {
                path = await IamHelper.AppendOidcReturnContextAsync(path, clientId, redirectUri, _defaultOidcClientResolver);
            }

            if (!IamHelper.TryBuildUserActionUrl(
                    config, path, out var accountActivationUri, _httpContextAccessor,
                    logger: _logger, appConfiguration: _configuration))
            {
                _logger.LogWarning("Activation URL could not be built for user {Id}", user.ItemId);
                return false;
            }

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            var emailPurpose = string.IsNullOrWhiteSpace(user.MailPurpose) ? "AccountActivation" : user.MailPurpose;
            var result = await _identityAccessManagementService.SendActivationToEmailAsync(user, accountActivationUri, emailPurpose);

            await _userRepository.InsertUserKeyMapAsync(new UserKeyMap
            {
                ItemId = Guid.NewGuid().ToString(),
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                Value = accountActivationUri,
                MailPurpose = emailPurpose
            });

            _logger.LogInformation("Send Activation for {Id} is {Send}", user.ItemId, result ? "sent" : "not sent");
            return result;
        }

        private async Task<bool> PublishUserActivityAsync(User user, MutationEventType mutationEventType)
        {
            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = user.ItemId,
                Category = UserActivityCategory.Resource,
                Event = ResolveTimelineEvent(user, mutationEventType),
                Source = "iam-user-mutation",
                Entity = "User",
                EntityId = user.ItemId
            });
            return true;
        }

        public async Task<BaseMutationResponse> UpdateUserAccessControlAsync(UpdateUserAccessControlRequest command)
        {
            _logger.LogInformation("Update User Access Control start");

            var user = await _userRepository.GetUserByIdAsync(command.UserId);
            if (user == null)
            {
                _logger.LogInformation("Update User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.UserId), "Not found" }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();
            var organizationId = string.IsNullOrWhiteSpace(command.OrganizationId)
                ? blocksContext?.OrganizationId
                : command.OrganizationId;

            // Named explicitly, the way the revoke side already does. Without it a caller carrying
            // no organization reaches the write with a null key and the roles dictionary throws.
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                _logger.LogInformation("Update User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.OrganizationId), "OrganizationId is required" }
                    }
                };
            }

            if (!string.Equals(blocksContext?.OrganizationId, DefaultOrganizationId, StringComparison.Ordinal)
                && !string.Equals(blocksContext?.OrganizationId, organizationId, StringComparison.Ordinal))
            {
                _logger.LogInformation("Update User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.OrganizationId), "Other org user can not add/update" }
                    }
                };
            }

            if (!string.Equals(organizationId, DefaultOrganizationId, StringComparison.Ordinal))
            {
                var organization = await _resourceRepository.GetOrganizationById(organizationId);
                if (organization == null)
                {
                    _logger.LogInformation("Update User Access Control end -- Validation Error");
                    return new BaseMutationResponse
                    {
                        Errors = new Dictionary<string, string>
                        {
                            { nameof(command.OrganizationId), "Organization not found" }
                        }
                    };
                }
            }

            if (command.Permissions != null && command.Permissions.Count > MaxPermissionsPerUser)
            {
                _logger.LogInformation("Update User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.Permissions), $"A maximum of {MaxPermissionsPerUser} permissions can be added with any user permission." }
                    }
                };
            }

            GrantOrganization(user, organizationId, command.Roles, command.Permissions);

            user.LastUpdatedDate = DateTime.UtcNow;
            user.LastUpdatedBy = blocksContext?.UserId ?? user.ItemId;

            var result = await _userRepository.UpdateUserAsync(user);
            if (!result)
            {
                _logger.LogError("Update User Access Control end -- Repository Error for UserId {UserId}", command.UserId);
                return new BaseMutationResponse();
            }

            //await SendEvent(user.ItemId, MutationEventType.Update);

            //await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            //{
            //    UserId = user.ItemId,
            //    Category = UserActivityCategory.Resource,
            //    Event = isAddToOrganization ? "USER_ACCESS_UPDATED" : "USER_ACCESS_REVOKED",
            //    Source = "iam-user-access-control",
            //    Entity = "User",
            //    EntityId = user.ItemId,
            //    Metadata = new Dictionary<string, string>
            //    {
            //        { "organizationId", organizationId ?? string.Empty },
            //        { "rolesAdded", string.Join(",", command.Roles ?? new List<string>()) },
            //        { "permissionsAdded", string.Join(",", command.Permissions ?? new List<string>()) }
            //    }
            //});

            _logger.LogInformation("Update User Access Control end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        public async Task<BaseMutationResponse> RevokeUserAccessControlAsync(RevokeUserAccessControlRequest command)
        {
            _logger.LogInformation("Revoke User Access Control start");

            if (string.IsNullOrWhiteSpace(command.UserId))
            {
                _logger.LogInformation("Revoke User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.UserId), "UserId is required" }
                    }
                };
            }

            var user = await _userRepository.GetUserByIdAsync(command.UserId);
            if (user == null)
            {
                _logger.LogInformation("Revoke User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.UserId), "Not found" }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();
            var organizationId = string.IsNullOrWhiteSpace(command.OrganizationId)
                ? blocksContext?.OrganizationId
                : command.OrganizationId;

            if (string.IsNullOrWhiteSpace(organizationId))
            {
                _logger.LogInformation("Revoke User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.OrganizationId), "OrganizationId is required" }
                    }
                };
            }

            if (string.Equals(command.UserId, blocksContext?.UserId, StringComparison.Ordinal))
            {
                _logger.LogInformation("Revoke User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.UserId), "You cannot revoke your own access" }
                    }
                };
            }

            if (!string.Equals(blocksContext?.OrganizationId, DefaultOrganizationId, StringComparison.Ordinal)
                && !string.Equals(blocksContext?.OrganizationId, organizationId, StringComparison.Ordinal))
            {
                _logger.LogInformation("Revoke User Access Control end -- Validation Error");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(command.OrganizationId), "Other org user can not revoke" }
                    }
                };
            }

            if (!string.Equals(organizationId, DefaultOrganizationId, StringComparison.Ordinal))
            {
                var organization = await _resourceRepository.GetOrganizationById(organizationId);
                if (organization == null)
                {
                    _logger.LogInformation("Revoke User Access Control end -- Validation Error");
                    return new BaseMutationResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { nameof(command.OrganizationId), "Organization not found" }
                        }
                    };
                }
            }

            user.OrganizationIds.Remove(organizationId);
            user.Roles.Remove(organizationId);
            user.Permissions.Remove(organizationId);

            user.LastUpdatedDate = DateTime.UtcNow;
            user.LastUpdatedBy = blocksContext?.UserId ?? user.ItemId;

            var result = await _userRepository.UpdateUserAsync(user);
            if (!result)
            {
                _logger.LogError("Revoke User Access Control end -- Repository Error for UserId {UserId}", command.UserId);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Repository", "Failed to update user" }
                    }
                };
            }

            await SendEvent(user.ItemId, MutationEventType.Update);

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = user.ItemId,
                Category = UserActivityCategory.Resource,
                Event = "USER_ACCESS_REVOKED",
                Source = "iam-user-access-control",
                Entity = "User",
                EntityId = user.ItemId,
                Metadata = new Dictionary<string, string>
                {
                    { "organizationId", organizationId }
                }
            });

            _logger.LogInformation("Revoke User Access Control end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        public async Task<bool> CreateUserByEmailAsync(CreateUserByEmailEvent @event)
        {
            _logger.LogInformation("User creation start from CreateUserByEmail");
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            var email = NormalizeEmail(@event.Email);

            var organizationId = (tenantConfig?.IsMultiOrgEnabled ?? false)
                ? (string.IsNullOrWhiteSpace(@event.OrganizationId) ? DefaultOrganizationId : @event.OrganizationId)
                : DefaultOrganizationId;

            var roles = @event.Roles ?? tenantConfig?.DefaultRolesForNewUserOnSignUp ?? new List<string>();
            var permissions = @event.Permissions ?? tenantConfig?.DefaultPermissionsForNewUserOnSignUp ?? new List<string>();

            if (ValidatePermissionCount(permissions) != null)
            {
                _logger.LogInformation("User creation end -- Permission Count Error -- CreateUserByEmail");
                return false;
            }

            // Inviting an email that already has an account is a join, not a signup: the invited
            // organization is granted on that account rather than opening a second one for the
            // same person.
            var existingUser = await TryGrantOrganizationToExistingUserAsync(email, organizationId, roles, permissions);

            if (existingUser != null)
            {
                _logger.LogInformation("User already exists for CreateUserByEmail; granted the invited organization instead of creating a duplicate");

                // An account that cannot sign in yet still needs an activation key, even though it exists.
                if (RequiresActivation(existingUser))
                {
                    var (existingUserKey, existingUserKeyExpiry) = await CreateUserByEmailActivationProcessAsync(existingUser, @event.EventType);
                    await SendCreateUserByEmailPostEventAsync(@event, existingUser.ItemId, existingUserKey, existingUserKeyExpiry);
                }
                else
                {
                    await SendCreateUserByEmailPostEventAsync(@event, existingUser.ItemId, string.Empty, null);
                }

                return true;
            }

            var command = new CreateUserRequest
            {
                Email = email,
                UserCreationType = UserCreationType.Service,
                MailPurpose = @event.EventType,
                OrganizationId = organizationId,
                Roles = roles,
                Permissions = permissions
            };

            string itemId;
            try
            {
                itemId = await ProcessCreateUserAsync(command);
            }
            catch (ValidationException)
            {
                _logger.LogInformation("User creation end -- Signup Policy Validation Error -- CreateUserByEmail");
                return false;
            }

            await ProcessCreateUserByEmailAfterActionAsync(@event, itemId);

            _logger.LogInformation("User creation end -- Success -- CreateUserByEmail");
            return true;
        }

        public async Task<bool> ProcessCreateUserByEmailAfterActionAsync(CreateUserByEmailEvent @event, string userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId);

            var (key, keyExpiresAtUtc) = await CreateUserByEmailActivationProcessAsync(user, @event.EventType);

            await PublishUserActivityAsync(user, MutationEventType.Create);

            await SendCreateUserByEmailPostEventAsync(@event, userId, key, keyExpiresAtUtc);

            return true;
        }

        /// <summary>An account that is inactive or unverified cannot sign in yet, so it still needs an activation key.</summary>
        private static bool RequiresActivation(User user) => !user.Active || !user.IsVerified;

        private async Task SendCreateUserByEmailPostEventAsync(CreateUserByEmailEvent @event, string userId, string key, DateTime? keyExpiresAtUtc)
        {
            await _identityAccessManagementService.SendToQueueAsync(@event.EventQueue, new CreateUserByEmailPostEvent
            {
                Key = key,
                UserId = userId,
                EventType = @event.EventType,
                TenantId = @event.TenantId,
                ForceInvitation = @event.ForceInvitation,
                KeyExpiresAtUtc = keyExpiresAtUtc,
            });
        }

        public async Task<(string key, DateTime expiresAtUtc)> CreateUserByEmailActivationProcessAsync(User user, string eventType)
        {
            var config = await _userRepository.GetIamConfigurationAsync();

            var key = Guid.NewGuid().ToString("n");
            var expiresAtUtc = DateTime.UtcNow.AddMinutes(config.ActivationUrlLifetimeInMinutes);

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            await _userRepository.InsertUserKeyMapAsync(new UserKeyMap
            {
                ItemId = Guid.NewGuid().ToString(),
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.UtcNow,
                ExpireDate = expiresAtUtc,
                MailPurpose = eventType
            });

            return (key, expiresAtUtc);
        }

        async Task<TenantConfiguration> IUserManagementMutationService.GetTenantConfigurationAsync()
        {
            return await _resourceRepository.GetTenantConfigurationAsync();
        }

        public async Task<BaseMutationResponse> CreateUserFromSsoAsync(CreateUserViaSsoRequest command)
        {
            _logger.LogInformation("User creation start");

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            command.OrganizationId = tenantConfig?.IsMultiOrgEnabled ?? false 
            ? string.IsNullOrWhiteSpace(command.OrganizationId) ? DefaultOrganizationId : command.OrganizationId
            : DefaultOrganizationId;
            var organization = null as Organization;
            if (command.OrganizationId != DefaultOrganizationId)
            {
                organization = await _resourceRepository.GetOrganizationById(command.OrganizationId);
            }


            if(command.Roles == null || command.Roles.Count == 0)
            {
                command.Roles = organization != null && organization.DefaultRoleForMembers != null && organization.DefaultRoleForMembers.Count > 0
                    ? organization.DefaultRoleForMembers
                    : new List<string>();
            }
            
            if(command.Permissions == null)
            {
                command.Permissions = organization != null && organization.DefaultPermissionsForMembers != null && organization.DefaultPermissionsForMembers.Count > 0
                    ? organization.DefaultPermissionsForMembers
                    : new List<string>();
            }

            // SSO consent reaches this method on every exchange, not only for unknown emails, so
            // without a lookup here each sign-in minted another account. An email that already has
            // one is signing in, not signing up: grant the organization and skip the welcome mail.
            var existingUser = await TryGrantOrganizationToExistingUserAsync(
                command.Email,
                command.OrganizationId,
                command.Roles,
                command.Permissions);

            if (existingUser != null)
            {
                _logger.LogInformation(
                    "User creation end -- email already has an account; granted organization {OrganizationId} instead of creating a duplicate via SSO",
                    command.OrganizationId);

                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = existingUser.ItemId
                };
            }

            var itemId = await ProcessSsoUserAsync(command);

            _logger.LogInformation("User mutation event -- initiate");
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreateUserViaSsoEvent>
                {
                    ConsumerName = IdpConstants.IamUserQueue,
                    Payload = new CreateUserViaSsoEvent
                    {
                        ItemId = itemId,
                        Action = MutationEventType.Create,
                        MailPurpose = command.MailPurpose,
                        SendWelcomeMail = command.SendWelcomeMail
                    }
                }
            );

            _logger.LogInformation("User creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        public async Task<string> ProcessSsoUserAsync(CreateUserViaSsoRequest command)
        {
            var blocksContext = BlocksContext.GetContext();
            var id = string.IsNullOrWhiteSpace(command.UserId) ? Guid.NewGuid().ToString() : command.UserId;
            var tenantId = blocksContext?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            
            var normalizedEmail = NormalizeEmail(command.Email);

            var user = new User
            {
                ItemId = id,
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId ?? id,
                Email = normalizedEmail,
                UserName = normalizedEmail,
                Password = _identityAccessManagementService.HashPassword(Guid.NewGuid().ToString(), tenant?.TenantSalt),
                PasswordSetTime = DateTime.Now,
                PasswordChangedAtUtc = DateTime.UtcNow,
                LastCredentialRotationAtUtc = DateTime.UtcNow,
                PhoneNumber = command.PhoneNumber ?? string.Empty,
                Language = command.Language ?? "en-US",
                Salutation = command.Salutation ?? string.Empty,
                FirstName = command.FirstName ?? string.Empty,
                LastName = command.LastName ?? string.Empty,
                Platform = command.Platform,
                Roles = new Dictionary<string, List<string>> { [command.OrganizationId] = command.Roles ?? new List<string>() },
                Permissions = new Dictionary<string, List<string>> { [command.OrganizationId] = command.Permissions ?? new List<string>() },
                OrganizationIds = new List<string> { command.OrganizationId },
                UserCreationType = command.UserCreationType,
                UserPassType = UserPassType.None,
                Tags = new List<string>(),
                VerifiedType = UserVerifiedType.None,
                ProfileImageUrl = command.ProfileImageUrl,
                ProfileImageId = command.ProfileImageId,
                AllowedLogInType = command.AllowedLogInType,
                MailPurpose = command.MailPurpose,
                Active = command.Active,
                IsVerified = command.IsVerified,
                Status = command.Active ? UserLifecycleStatus.Active : UserLifecycleStatus.Suspended,
                ProvisioningSource = UserProvisioningSource.Social,
                EmailVerifiedAtUtc = DateTime.UtcNow,
                FailedLoginCount = 0,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                TokenVersion = 1,
                ExternalUserId = command.ExternalUserId,
                ExternalIdentities = string.IsNullOrWhiteSpace(command.ExternalUserId)
                    ? new List<ExternalIdentity>()
                    : new List<ExternalIdentity>
                    {
                        new ExternalIdentity
                        {
                            Provider = command.Platform,
                            ProviderUserId = command.ExternalUserId,
                            Issuer = command.Platform,
                            LinkedAtUtc = DateTime.UtcNow
                        }
                    },
                Attributes = AttributeNormalizer.Normalize(command.Attributes, AttributePolicy.Internal),
            };
            await _userRepository.CreateUserAsync(user);

            return user.ItemId;
        }

        private static UserProvisioningSource ResolveProvisioningSource(UserCreationType creationType)
        {
            return creationType switch
            {
                UserCreationType.Social => UserProvisioningSource.Social,
                UserCreationType.Api => UserProvisioningSource.API,
                _ => UserProvisioningSource.Manual
            };
        }

        private static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }

        private static string NormalizeIdentity(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        public async Task ExecuteUserMutationViaSsoCommandAsync(CreateUserViaSsoEvent command)
        {
            _logger.LogInformation("User Mutation event -- initiate");

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);
            if (command.SendWelcomeMail)
            {
                await SendPostEventAsync(user, command.MailPurpose);
            }
            await PublishUserActivityAsync(user, command.Action);
        }

        private static string ResolveTimelineEvent(User user, MutationEventType mutationEventType)
        {
            return mutationEventType switch
            {
                MutationEventType.Create => "USER_CREATED",
                MutationEventType.Update when user.Active => "USER_UPDATED",
                MutationEventType.Update => "USER_STATUS_UPDATED",
                MutationEventType.Delete when !user.Active => "USER_DEACTIVATED",
                MutationEventType.Delete => "USER_DELETED",
                _ => "USER_ACTIVITY"
            };
        }

        #region Bulk role change

        /// <summary>Request-shape guard, not a business rule: nobody may hold "only 50" roles.</summary>
        private const int MaxRolesPerBulkRequest = 50;

        /// <summary>Upper bound on a hand-picked target, so one payload cannot be unbounded.</summary>
        private const int MaxUserIdsPerBulkRequest = 1000;

        /// <summary>
        /// The blast radius one submit may have. A truncated count would be a number the operator
        /// acts on that does not describe what would happen, so over-cap is a rejection rather than a
        /// partial success.
        /// </summary>
        private const int MaxBulkMatchedUsers = 5000;

        /// <summary>Resolved ids per queue message. Bounds message size and per-message time.</summary>
        private const int BulkRoleChunkSize = 500;

        /// <summary>Page size used while walking the matched set; an implementation detail only.</summary>
        private const int BulkResolutionPageSize = 1000;

        /// <summary>
        /// Apply a role delta to one user, in memory. <strong>Pure</strong>: it touches nothing but the
        /// <paramref name="user"/> instance handed to it and never persists.
        /// <para>
        /// The preview and the worker both call this one function, which is the only way to guarantee
        /// the count the operator approved is the change the worker writes. It is public rather than
        /// internal because the worker lives in another assembly and the test project has no
        /// <c>InternalsVisibleTo</c>.
        /// </para>
        /// <para>
        /// Idempotent by construction: applying the same add/remove lists twice produces no change
        /// the second time, so a redelivered queue message converges instead of double-writing.
        /// </para>
        /// </summary>
        /// <returns><c>true</c> when the user's role list for this organization actually changed.</returns>
        public static bool ApplyRoleDelta(
            User user,
            string organizationId,
            IReadOnlyList<string> addRoles,
            IReadOnlyList<string> removeRoles)
        {
            var current = user.Roles.TryGetValue(organizationId, out var existing) && existing is not null
                ? new List<string>(existing)
                : new List<string>();

            var next = current.Where(r => !removeRoles.Contains(r, StringComparer.Ordinal)).ToList();

            // Append-only, so a user's existing order survives and re-running the same delta is a
            // no-op rather than a reshuffle.
            foreach (var role in addRoles)
            {
                if (!next.Contains(role, StringComparer.Ordinal))
                {
                    next.Add(role);
                }
            }

            if (next.SequenceEqual(current, StringComparer.Ordinal))
            {
                return false;
            }

            // Matching the single-user grant path: writing a role into an organization implies
            // membership of it, rather than silently dropping the write.
            if (!user.OrganizationIds.Contains(organizationId))
            {
                user.OrganizationIds.Add(organizationId);
            }

            user.Roles[organizationId] = next;
            return true;
        }

        public async Task<BulkRoleChangePreviewResponse> PreviewBulkRoleChangeAsync(BulkRoleChangeRequest command)
        {
            _logger.LogInformation("Bulk role change preview start");

            var (errors, plan) = await ValidateBulkRoleChangeAsync(command);
            if (errors is not null)
            {
                _logger.LogInformation("Bulk role change preview end -- Validation Error");
                return new BulkRoleChangePreviewResponse { Errors = errors };
            }

            var (users, overCap) = await ResolveBulkRoleTargetAsync(plan!);
            if (overCap)
            {
                _logger.LogInformation("Bulk role change preview end -- over the matched cap");
                return new BulkRoleChangePreviewResponse { Errors = OverMatchedCapError() };
            }

            long affected = 0;
            foreach (var user in users)
            {
                // Safe to mutate: these are documents loaded into memory and thrown away. Nothing on
                // this path writes, which a collection diff in the E2E steps proves.
                if (ApplyRoleDelta(user, plan!.OrganizationId, plan.AddRoles, plan.RemoveRoles))
                {
                    affected++;
                }
            }

            // Counted from the set actually evaluated rather than from CountDocuments, so the
            // matched == affected + unchanged invariant holds even if the collection moves between
            // the count and the pages.
            long matched = users.Count;

            _logger.LogInformation(
                "Bulk role change preview end -- matched {Matched}, affected {Affected}",
                matched,
                affected);

            return new BulkRoleChangePreviewResponse
            {
                IsSuccess = true,
                MatchedCount = matched,
                AffectedCount = affected,
                UnchangedCount = matched - affected
            };
        }

        public async Task<BulkRoleChangeSubmitResponse> SubmitBulkRoleChangeAsync(BulkRoleChangeRequest command)
        {
            _logger.LogInformation("Bulk role change submit start");

            // Re-run rather than trust the preview: a console may submit without previewing, and the
            // data can move between the two calls. Sharing one validator is what keeps the two
            // endpoints' error strings identical.
            var (errors, plan) = await ValidateBulkRoleChangeAsync(command);
            if (errors is not null)
            {
                _logger.LogInformation("Bulk role change submit end -- Validation Error");
                return new BulkRoleChangeSubmitResponse { Errors = errors };
            }

            var (users, overCap) = await ResolveBulkRoleTargetAsync(plan!);
            if (overCap)
            {
                _logger.LogInformation("Bulk role change submit end -- over the matched cap");
                return new BulkRoleChangeSubmitResponse { Errors = OverMatchedCapError() };
            }

            var userIds = users
                .Select(u => u.ItemId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var batchId = $"b_{Guid.NewGuid():N}"[..10];
            var tenantId = BlocksContext.GetContext()?.TenantId;

            try
            {
                for (var offset = 0; offset < userIds.Count; offset += BulkRoleChunkSize)
                {
                    var chunk = userIds.Skip(offset).Take(BulkRoleChunkSize).ToList();

                    await _messageClient.SendToConsumerAsync(
                        new ConsumerMessage<BulkUserRoleChangeEvent>
                        {
                            ConsumerName = IdpConstants.IamBulkRoleQueue,
                            Payload = new BulkUserRoleChangeEvent
                            {
                                BatchId = batchId,
                                OrganizationId = plan!.OrganizationId,
                                TenantId = tenantId,
                                AddRoles = plan.AddRoles,
                                RemoveRoles = plan.RemoveRoles,
                                UserIds = chunk
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                // The console must never show "submitted" for work that was never queued. Chunks
                // already published are left to run -- they are legitimate work -- and this response
                // deliberately does not claim otherwise.
                _logger.LogError(
                    ex,
                    "Bulk role change submit end -- publishing failed for batch {BatchId}",
                    batchId);

                return new BulkRoleChangeSubmitResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Queue", "The bulk role change could not be queued. Try again." }
                    }
                };
            }

            _logger.LogInformation(
                "Bulk role change submit end -- batch {BatchId} queued for {Matched} users",
                batchId,
                userIds.Count);

            return new BulkRoleChangeSubmitResponse
            {
                IsSuccess = true,
                BatchId = batchId,
                MatchedCount = userIds.Count
            };
        }

        public async Task ApplyBulkRoleChangeAsync(BulkUserRoleChangeEvent command)
        {
            _logger.LogInformation(
                "Bulk role change apply start -- batch {BatchId}, {Count} users",
                command.BatchId,
                command.UserIds.Count);

            foreach (var userId in command.UserIds)
            {
                // Per-user failures are logged and swallowed. Rethrowing would have the broker
                // redeliver the whole chunk, re-scanning hundreds of healthy users for one bad id and
                // looping forever on a permanently failing one.
                try
                {
                    var user = await _userRepository.GetUserByIdAsync(userId);
                    if (user is null)
                    {
                        _logger.LogWarning(
                            "Bulk role change -- batch {BatchId}, user {UserId}: failed (not found)",
                            command.BatchId,
                            userId);
                        continue;
                    }

                    if (!ApplyRoleDelta(user, command.OrganizationId, command.AddRoles, command.RemoveRoles))
                    {
                        _logger.LogInformation(
                            "Bulk role change -- batch {BatchId}, user {UserId}: no-op",
                            command.BatchId,
                            userId);
                        continue;
                    }

                    user.LastUpdatedDate = DateTime.UtcNow;
                    user.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? user.ItemId;

                    var updated = await _userRepository.UpdateUserAsync(user);

                    if (updated)
                    {
                        _logger.LogInformation(
                            "Bulk role change -- batch {BatchId}, user {UserId}: applied",
                            command.BatchId,
                            userId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Bulk role change -- batch {BatchId}, user {UserId}: failed (update rejected)",
                            command.BatchId,
                            userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Bulk role change -- batch {BatchId}, user {UserId}: failed",
                        command.BatchId,
                        userId);
                }
            }

            _logger.LogInformation("Bulk role change apply end -- batch {BatchId}", command.BatchId);
        }

        /// <summary>
        /// A validated request, with the lists normalised once so the preview and the submit path
        /// cannot normalise them differently.
        /// </summary>
        private sealed record BulkRoleChangePlan(
            string OrganizationId,
            List<string> AddRoles,
            List<string> RemoveRoles,
            BulkRoleTarget Target);

        private static Dictionary<string, string> OverMatchedCapError() =>
            new()
            {
                { "Matched", $"This filter matches more than {MaxBulkMatchedUsers} users. Narrow it and try again." }
            };

        /// <summary>
        /// The single validator both bulk endpoints run. Shared rather than duplicated so the submit
        /// response can never disagree with the preview the operator approved.
        /// </summary>
        private async Task<(IDictionary<string, string>? Errors, BulkRoleChangePlan? Plan)> ValidateBulkRoleChangeAsync(
            BulkRoleChangeRequest command)
        {
            var organizationId = command.OrganizationId;

            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return (Error(nameof(command.OrganizationId), "OrganizationId is required"), null);
            }

            // Reused verbatim from the single-user path, so the cross-organization rule cannot drift
            // between the two.
            var callerGuard = ValidateCallerMayWriteToOrganization(organizationId);
            if (callerGuard is not null)
            {
                return (callerGuard.Errors, null);
            }

            // Blank entries are dropped rather than rejected: the contract has no error string for
            // them, and an empty string is not a role anyone can hold. If dropping empties both
            // lists, the "at least one role" rule below reports it.
            var addRoles = Clean(command.AddRoles);
            var removeRoles = Clean(command.RemoveRoles);

            if (addRoles.Count == 0 && removeRoles.Count == 0)
            {
                return (Error("Roles", "At least one role to add or remove is required"), null);
            }

            if (addRoles.Count > MaxRolesPerBulkRequest || removeRoles.Count > MaxRolesPerBulkRequest)
            {
                return (Error("Roles", $"A maximum of {MaxRolesPerBulkRequest} roles is allowed per request"), null);
            }

            if (HasDuplicates(addRoles) || HasDuplicates(removeRoles))
            {
                return (Error("Roles", "Duplicate role in the same request"), null);
            }

            if (addRoles.Intersect(removeRoles, StringComparer.Ordinal).Any())
            {
                return (Error("Roles", "The same role cannot be both added and removed"), null);
            }

            var target = command.Target;
            var userIds = Clean(target?.UserIds);
            var hasUserIds = userIds.Count > 0;
            var hasFilter = target?.Filter is not null;

            if (hasUserIds == hasFilter)
            {
                return (Error("Target", "Exactly one of userIds or filter is required"), null);
            }

            if (hasUserIds)
            {
                if (userIds.Count > MaxUserIdsPerBulkRequest)
                {
                    return (Error("Target", $"A maximum of {MaxUserIdsPerBulkRequest} userIds is allowed per request"), null);
                }

                if (HasDuplicates(userIds))
                {
                    return (Error("Target", "Duplicate userId in the same request"), null);
                }
            }

            // "default" is the tenant-wide organization and has no Organization document, exactly as
            // the single-user path already assumes.
            if (!string.Equals(organizationId, DefaultOrganizationId, StringComparison.Ordinal)
                && _resourceRepository is not null)
            {
                var organization = await _resourceRepository.GetOrganizationById(organizationId);
                if (organization is null)
                {
                    return (Error(nameof(command.OrganizationId), "Organization not found"), null);
                }
            }

            var plan = new BulkRoleChangePlan(
                organizationId,
                addRoles,
                removeRoles,
                new BulkRoleTarget { UserIds = hasUserIds ? userIds : null, Filter = target!.Filter });

            return (null, plan);

            static Dictionary<string, string> Error(string field, string message) =>
                new() { { field, message } };

            static List<string> Clean(List<string>? values) =>
                values is null
                    ? new List<string>()
                    : values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

            static bool HasDuplicates(List<string> values) =>
                values.Distinct(StringComparer.Ordinal).Count() != values.Count;
        }

        /// <summary>
        /// Walk the matched set through the same filter/scope path the user list uses, so the console
        /// and the mutation can never disagree about who matched.
        /// </summary>
        /// <returns>
        /// Every matched user, and whether the match exceeded <see cref="MaxBulkMatchedUsers"/> -- in
        /// which case the list is deliberately empty, because partial counts are worse than none.
        /// </returns>
        private async Task<(List<User> Users, bool OverCap)> ResolveBulkRoleTargetAsync(BulkRoleChangePlan plan)
        {
            // The requested organization is passed explicitly rather than taken from the filter body,
            // so a filter can never widen the scope beyond the one organization the caller named and
            // was authorised for.
            var scope = UserListOrganizationScope.Resolve(
                BlocksContext.GetContext()?.OrganizationId,
                [plan.OrganizationId]);

            if (scope.Kind == UserListScopeKind.Denied)
            {
                // A token carrying no organization is answered without touching the database, the way
                // the user list already answers it.
                return (new List<User>(), false);
            }

            var filter = plan.Target.Filter ?? new GetUsersFilter { UserIds = plan.Target.UserIds! };

            var users = new List<User>();
            long totalCount = 0;

            for (var page = 0; ; page++)
            {
                var query = new GetUsersRequest
                {
                    Filter = filter,
                    Page = page,
                    PageSize = BulkResolutionPageSize
                };

                var (data, count) = await _userRepository.GetUsersAsync<User, GetUsersRequest>(query, scope);

                if (page == 0)
                {
                    totalCount = count;
                    if (totalCount > MaxBulkMatchedUsers)
                    {
                        return (new List<User>(), true);
                    }
                }

                var items = data?.ToList() ?? new List<User>();
                users.AddRange(items);

                if (items.Count < BulkResolutionPageSize || users.Count >= totalCount)
                {
                    break;
                }
            }

            return (users, false);
        }

        #endregion

        private async Task<bool> SendPostEventAsync(User user, string mailPurpose)
        {
            return await _identityAccessManagementService.SendAccountActivationEmailAsync(user, mailPurpose);
        }

    }
}

