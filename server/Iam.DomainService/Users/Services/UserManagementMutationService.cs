using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Utilities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Iam.DomainService.Users
{
    public class UserManagementMutationService : IUserManagementMutationService
    {
        private readonly ILogger<UserManagementMutationService> _logger;
        private readonly IValidator<CreateUserRequest> _createValidator;
        private readonly IValidator<UpdateUserRequest> _updateValidator;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly IUserRepository _userRepository;
        private readonly IIdentityAccessManagementRepository? _identityAccessManagementRepository;
        private readonly IResourceRepository? _resourceRepository;
        private readonly IMessageClient _messageClient;
        private readonly ICacheClient _cacheClient;
        private BlocksContext _blocksContext;

        public UserManagementMutationService(
            ILogger<UserManagementMutationService> logger,
            IValidator<CreateUserRequest> createValidator,
            IValidator<UpdateUserRequest> updateValidator,
            IIdentityAccessManagementService identityAccessManagementService,
            IUserRepository userRepository,
            IMessageClient messageClient,
            ICacheClient cacheClient,
            IIdentityAccessManagementRepository? identityAccessManagementRepository = null,
            IResourceRepository? resourceRepository = null
        )
        {
            _logger = logger;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _identityAccessManagementService = identityAccessManagementService;
            _userRepository = userRepository;
            _messageClient = messageClient;
            _cacheClient = cacheClient;
            _identityAccessManagementRepository = identityAccessManagementRepository;
            _resourceRepository = resourceRepository;
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

            _blocksContext = BlocksContext.GetContext();

            string itemId;
            try
            {
                itemId = await ProcessAsync(command);
            }
            catch (ValidationException ex)
            {
                _logger.LogInformation("User creation end -- Signup Policy Validation Error");
                return new BaseMutationResponse
                {
                    Errors = ex.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            await SendEvent(itemId, MutationEventType.Create);

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<UpdateResourceUsageCommand>
            {
                ConsumerName = Constants.IdentifierQueue,
                Payload = new UpdateResourceUsageCommand
                {
                    Resource = "blocks-idp-api::iam::create",
                    TenantId = _blocksContext.TenantId,
                    Amount = 1
                }
            });

            _logger.LogInformation("User creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        private async Task SendEvent(string itemId, MutationEventType mutationEventType)
        {
            _logger.LogInformation("User mutation event -- initiate");
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<UserMutationEvent>
                {
                    ConsumerName = Constants.IamQueue,
                    Payload = new UserMutationEvent
                    {
                        ItemId = itemId,
                        Action = mutationEventType
                    }
                }
            );
            _logger.LogInformation("User mutation event -- sent");
        }

        public async Task<string> ProcessAsync(CreateUserRequest command)
        {
            var user = await _userRepository.GetUserByEmailAsync(command.Email);
            var defaultOrganizationId = ResolveOrganizationId(command.OrgId, command.Roles, command.Permissions);
            var orgConfig = await GetOrganizationConfigAsync(command, defaultOrganizationId);
            var signUpPolicyError = await ValidateSignUpPolicyAsync(command, user, defaultOrganizationId, orgConfig);

            if (signUpPolicyError != null)
            {
                throw new ValidationException([signUpPolicyError]);
            }

            var defaultRoles = ResolveDefaultRoles(orgConfig);
            var normalizedRoles = NormalizeOrgClaimMap(command.Roles, defaultOrganizationId, ["user"]);
            var normalizedPermissions = NormalizeOrgClaimMap(command.Permissions, defaultOrganizationId, []);

            if (normalizedRoles.TryGetValue(defaultOrganizationId, out var scopedRoles)
                && scopedRoles.Count == 0
                && defaultRoles.Count > 0)
            {
                normalizedRoles[defaultOrganizationId] = defaultRoles;
            }

            if(user is not null)
            {
                var organizationId = defaultOrganizationId;

                if (!user.OrganizationIds.Contains(organizationId))
                {
                    user.OrganizationIds = [.. user.OrganizationIds, organizationId];
                }

                if (!user.Roles.ContainsKey(organizationId) || user.Roles[organizationId].Count == 0)
                {
                    user.Roles[organizationId] = normalizedRoles[organizationId];
                }

                if (!user.Permissions.ContainsKey(organizationId))
                {
                    user.Permissions[organizationId] = normalizedPermissions[organizationId];
                }

                await _userRepository.UpdateUserAsync(user);
                return user.ItemId;
            }

           user = await CreateNewUser(command, defaultRoles);
           return user.ItemId;
        }

        private async Task<User> CreateNewUser(CreateUserRequest command, List<string> defaultRoles)
        {
            var user = MapUser(command, defaultRoles);
            await _userRepository.CreateUserAsync(user);
            return user;
        }

        public User MapUser(CreateUserRequest command, List<string>? defaultRoles = null)
        {
            var id = Guid.NewGuid().ToString();
            var organizationId = ResolveOrganizationId(command.OrgId, command.Roles, command.Permissions);
            var roles = NormalizeOrgClaimMap(command.Roles, organizationId, defaultRoles is { Count: > 0 } ? defaultRoles : ["user"]);
            var permissions = NormalizeOrgClaimMap(command.Permissions, organizationId, []);

            var user = new User
            {
                ItemId = id,
                SubjectId = $"usr_{Guid.NewGuid():N}",
                CreatedDate = DateTime.Now,
                CreatedBy = _blocksContext?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = _blocksContext?.UserId ?? id,
                Email = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                UserName = (string.IsNullOrWhiteSpace(command.UserName) ? command.Email : command.UserName).ToLower(),
                Password = string.IsNullOrWhiteSpace(command.Password) ? string.Empty : _identityAccessManagementService.HashPassword(command.Password),
                PasswordSetTime = string.IsNullOrWhiteSpace(command.Password) ? DateTime.MinValue : DateTime.Now,
                PasswordChangedAtUtc = string.IsNullOrWhiteSpace(command.Password) ? null : DateTime.UtcNow,
                LastCredentialRotationAtUtc = string.IsNullOrWhiteSpace(command.Password) ? null : DateTime.UtcNow,
                PhoneNumber = command.PhoneNumber ?? string.Empty,
                Language = command.Language ?? "en-US",
                Salutation = command.Salutation ?? string.Empty,
                FirstName = command.FirstName ?? string.Empty,
                LastName = command.LastName ?? string.Empty,
                Platform = command.Platform,
                OrganizationIds = BuildOrganizationIds(roles, permissions, organizationId),
                Roles = roles,
                Permissions = permissions,
                UserCreationType = command.UserCreationType,
                UserPassType = command.UserPassType,
                Tags = command.Tags ?? [],
                VarifiedType = command.VarifiedType,
                ProfileImageUrl = command.ProfileImageUrl,
                ProfileImageId = command.ProfileImageId,
                AllowedLogInType = command.AllowedLogInType,
                MfaEnabled = command.MfaEnabled,
                UserMfaType = command.UserMfaType,
                MfaMethods = [],
                MailPurpose = string.IsNullOrWhiteSpace(command.MailPurpose) ? "AccountActivation" : command.MailPurpose,
                ProvisioningSource = ResolveProvisioningSource(command.UserCreationType),
                Status = command.VarifiedType == UserVarifiedType.None ? UserLifecycleStatus.PendingVerification : UserLifecycleStatus.Active,
                EmailVerifiedAtUtc = command.VarifiedType == UserVarifiedType.Email ? DateTime.UtcNow : null,
                PhoneVerifiedAtUtc = command.VarifiedType is UserVarifiedType.Sms or UserVarifiedType.WhatsApp ? DateTime.UtcNow : null,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                TokenVersion = 1,
                FailedLoginCount = 0,
                LastFailedLoginUtc = null,
                LockoutUntilUtc = null,
                TermsAcceptedAtUtc = null,
                PrivacyAcceptedAtUtc = null,
                ExternalIdentities = []
            };

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

            _blocksContext = BlocksContext.GetContext();

            user.Salutation = command.Salutation ?? string.Empty;
            user.FirstName = command.FirstName ?? string.Empty;
            user.LastName = command.LastName ?? string.Empty;
            user.PhoneNumber = command.PhoneNumber ?? string.Empty;
            user.LastUpdatedDate = DateTime.Now;
            user.LastUpdatedBy = _blocksContext?.UserId ?? user.ItemId;
            user.Tags = command.Tags ?? user.Tags;
            user.ProfileImageId = command.ProfileImageId ?? string.Empty;
            user.ProfileImageUrl = command.ProfileImageUrl ?? string.Empty;
            user.MfaEnabled = command.MfaEnabled;
            
            user.Roles = NormalizeOrgClaimMap(command.Roles, "default", ["user"]);
            user.Permissions = NormalizeOrgClaimMap(command.Permissions, "default", []);
            user.OrganizationIds = BuildOrganizationIds(user.Roles, user.Permissions, "default");

            if (command.MfaEnabled)
            {
                user.UserMfaType = command.UserMfaType;
            }


            var result = await _userRepository.UpdateUserAsync(user);

            if (!result)
            {
                _logger.LogInformation("User update end -- Error");
                return new BaseMutationResponse();
            }

            await SendEvent(user.ItemId, MutationEventType.Update);

            _logger.LogInformation("User update end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

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
                ConsumerName = Constants.IamQueue,
                Payload = new UserStatusChangedEvent
                {
                    UserId = request.UserId,
                    IsActive = false
                }
            }));

            await SendEvent(user.ItemId, MutationEventType.Delete);

            return new BaseResponse { IsSuccess = true };
        }

        public async Task UpdateUserByLoginInfoAsync(RefreshTokenEvent refreshTokenConsumer)
        {
            _logger.LogInformation("User Mutation event -- initiate to update login info");

            var user = await _userRepository.GetUserByIdAsync(refreshTokenConsumer.UserId);

            if (user == null)
            {
                _logger.LogError("User not found by this user id: {Id}", refreshTokenConsumer.UserId);
                return;
            }

            if (user.LogInCount == 0)
            {
                user.FirstLoggedInTime = DateTime.Now;
            }

            user.LogInCount += 1;
            user.LastLoggedInTime = DateTime.Now;
            user.LastLoggedInDeviceInfo = JsonSerializer.Serialize(refreshTokenConsumer.DeviceInformation);
            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;

            await _userRepository.UpdateUserAsync(user);

            _logger.LogInformation("User Mutation event -- end of the update login info");
        }

        public async Task ExecuteUserMutationCommandAsync(UserMutationEvent command)
        {
            _logger.LogInformation("User Mutation event -- initiate");

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);

            await SendActivationAsync(user);
            await SaveUserTimelineAsync(user, command.Action);
        }

        private async Task<bool> SendActivationAsync(User user)
        {
            _logger.LogInformation("Send Activation for {Id}", user.ItemId);
            var config = await _userRepository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var accountActivationUri = string.Format("{0}?code={1}&lang={2}", config.AccountActivationUrl, key, user.Language);

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            var emailPurpose = string.IsNullOrWhiteSpace(user.MailPurpose) ? "AccountActivation" : user.MailPurpose;
            var result = await _identityAccessManagementService.SendActivationToEmailAsync(user, accountActivationUri, emailPurpose, string.Empty);

            await _userRepository.InsertUserKeyMapAsync(new UserKeyMap
            {
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

        private async Task<bool> SaveUserTimelineAsync(User user, MutationEventType mutationEventType)
        {
            var blocksContext = BlocksContext.GetContext();
            var timeline = new UserTimeline
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = blocksContext?.UserId ?? user.CreatedBy,
                CreatedDate = DateTime.Now,
                CurrentData = user,
                Event = ResolveTimelineEvent(user, mutationEventType)
            };

            await _userRepository.InsertUserTimelineAsync(timeline);
            return true;
        }

        public async Task<BaseMutationResponse> SaveRolesAndPermissionsAsync(SaveRolesAndPermissionsRequest command)
        {
            _logger.LogInformation("SaveRolesAndPermissions start");

            var user = await _userRepository.GetUserByIdAsync(command.UserId);
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

            user.Roles = NormalizeOrgClaimMap(command.Roles, "default", ["user"]);
            user.Permissions = NormalizeOrgClaimMap(command.Permissions, "default", []);
            user.OrganizationIds = BuildOrganizationIds(user.Roles, user.Permissions, "default");
            var result = await _userRepository.UpdateUserAsync(user);

            if (!result)
            {
                _logger.LogInformation("SaveRolesAndPermissions end -- Error");
                return new BaseMutationResponse();
            }

            await SendEvent(user.ItemId, MutationEventType.Update);

            _logger.LogInformation("SaveRolesAndPermissions end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        public async Task<bool> CreateUserByEmailAsync(CreateUserByEmailEvent @event)
        {
            _logger.LogInformation("User creation start from CreateUserByEmail");

            var command = new CreateUserRequest
            {
                Email = @event.Email,
                UserCreationType = UserCreationType.Service,
                MailPurpose = @event.EventType,
                OrgId = "default",
                Roles = new Dictionary<string, List<string>> { ["default"] = ["user"] },
                Permissions = new Dictionary<string, List<string>> { ["default"] = [] }
            };

            _blocksContext = BlocksContext.GetContext();

            var validationResult = await _createValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("User creation end -- Validation Error -- CreateUserByEmail");
                return false;
            }

            string itemId;
            try
            {
                itemId = await ProcessAsync(command);
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

            var key = await CreateUserByEmailActivationProcessAsync(user, @event.EventType);

            await SaveUserTimelineAsync(user, MutationEventType.Create);

            await _identityAccessManagementService.SendToQueueAsync(@event.EventQueue, new CreateUserByEmailPostEvent
            {
                Key = key,
                UserId = userId,
                EventType = @event.EventType,
                ProjectKey = @event.ProjectKey,
            });

            return true;
        }

        public async Task<string> CreateUserByEmailActivationProcessAsync(User user, string eventType)
        {
            var config = await _userRepository.GetIamConfigurationAsync();

            var key = Guid.NewGuid().ToString("n");

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            await _userRepository.InsertUserKeyMapAsync(new UserKeyMap
            {
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                MailPurpose = eventType
            });

            return key;
        }

        public async Task<BaseMutationResponse> CreateUserViaSsoAsync(CreateUserViaSsoRequest command)
        {
            _logger.LogInformation("User creation start");

            _blocksContext = BlocksContext.GetContext();

            var fallbackOrganizationId = BlocksContext.GetContext()?.OrganizationId ?? "default";
            var orgConfig = await GetOrganizationConfigAsync(command.ProjectKey, fallbackOrganizationId);
            var signUpPolicyError = await ValidateSignUpPolicyAsync(command.UserCreationType, null, fallbackOrganizationId, orgConfig);

            if (signUpPolicyError != null)
            {
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { signUpPolicyError.PropertyName, signUpPolicyError.ErrorMessage }
                    }
                };
            }

            var itemId = await ProcessSsoUserAsync(command, ResolveDefaultRoles(orgConfig));

            _logger.LogInformation("User mutation event -- initiate");
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreateUserViaSsoEvent>
                {
                    ConsumerName = Constants.IamQueue,
                    Payload = new CreateUserViaSsoEvent
                    {
                        ItemId = itemId,
                        Action = MutationEventType.Create,
                        MailPurpose = command.MailPurpose,
                        SendWelcomeMail = command.SendWelcomeMail,
                        ProjectKey = command.ProjectKey
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
            return await ProcessSsoUserAsync(command, ["user"]);
        }

        public async Task<string> ProcessSsoUserAsync(CreateUserViaSsoRequest command, List<string> defaultRoles)
        {
            var id = Guid.NewGuid().ToString();
            var fallbackOrganizationId = BlocksContext.GetContext()?.OrganizationId ?? "default";
            var normalizedRoles = NormalizeOrgClaimMap(command.Roles, fallbackOrganizationId, defaultRoles.Count > 0 ? defaultRoles : ["user"]);
            var normalizedPermissions = NormalizeOrgClaimMap(command.Permissions, fallbackOrganizationId, []);
            var user = new User
            {
                ItemId = id,
                SubjectId = $"usr_{Guid.NewGuid():N}",
                CreatedDate = DateTime.Now,
                CreatedBy = _blocksContext?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = _blocksContext?.UserId ?? id,
                Email = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                UserName = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                Password = _identityAccessManagementService.HashPassword(Guid.NewGuid().ToString()),
                PasswordSetTime = DateTime.Now,
                PasswordChangedAtUtc = DateTime.UtcNow,
                LastCredentialRotationAtUtc = DateTime.UtcNow,
                PhoneNumber = command.PhoneNumber ?? string.Empty,
                Language = command.Language ?? "en-US",
                Salutation = command.Salutation ?? string.Empty,
                FirstName = command.FirstName ?? string.Empty,
                LastName = command.LastName ?? string.Empty,
                Platform = command.Platform,
                Roles = normalizedRoles,
                Permissions = normalizedPermissions,
                OrganizationIds = BuildOrganizationIds(normalizedRoles, normalizedPermissions, fallbackOrganizationId),
                UserCreationType = command.UserCreationType,
                UserPassType = UserPassType.None,
                Tags = [],
                VarifiedType = UserVarifiedType.None,
                ProfileImageUrl = command.ProfileImageUrl,
                ProfileImageId = command.ProfileImageId,
                AllowedLogInType = command.AllowedLogInType,
                MailPurpose = command.MailPurpose,
                Active = command.Active,
                IsVarified = command.IsVarified,
                Status = command.Active ? UserLifecycleStatus.Active : UserLifecycleStatus.Suspended,
                ProvisioningSource = UserProvisioningSource.Social,
                EmailVerifiedAtUtc = DateTime.UtcNow,
                FailedLoginCount = 0,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                TokenVersion = 1,
                ExternalUserId = command.ExternalUserId,
                ExternalIdentities = string.IsNullOrWhiteSpace(command.ExternalUserId)
                    ? []
                    :
                    [
                        new ExternalIdentity
                        {
                            Provider = command.Platform,
                            ProviderUserId = command.ExternalUserId,
                            Issuer = command.Platform,
                            LinkedAtUtc = DateTime.UtcNow
                        }
                    ],
                Department = command.DepartMent,
                EmployeeId = command.EmployeeId
            };
            await _userRepository.CreateUserAsync(user);

            return user.ItemId;
        }

        private static string ResolveOrganizationId(
            string? requestedOrganizationId,
            Dictionary<string, List<string>> roles,
            Dictionary<string, List<string>> permissions)
        {
            if (!string.IsNullOrWhiteSpace(requestedOrganizationId))
            {
                return requestedOrganizationId;
            }

            var roleOrgId = roles.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
            if (!string.IsNullOrWhiteSpace(roleOrgId))
            {
                return roleOrgId;
            }

            var permissionOrgId = permissions.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
            if (!string.IsNullOrWhiteSpace(permissionOrgId))
            {
                return permissionOrgId;
            }

            return "default";
        }

        private static Dictionary<string, List<string>> NormalizeOrgClaimMap(
            Dictionary<string, List<string>> source,
            string fallbackOrganizationId,
            IEnumerable<string> fallbackValues)
        {
            var normalized = source
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .ToDictionary(
                    entry => entry.Key,
                    entry => (entry.Value ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            if (normalized.Count == 0)
            {
                normalized[fallbackOrganizationId] = fallbackValues
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return normalized;
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

        private async Task<OrganizationConfig?> GetOrganizationConfigAsync(CreateUserRequest command, string organizationId)
        {
            var tenantId = ResolveTenantId(command.ProjectKey);
            if (_resourceRepository == null || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(organizationId))
            {
                return null;
            }

            return await _resourceRepository.GetOrganizationConfigAsync(tenantId, organizationId);
        }

        private async Task<OrganizationConfig?> GetOrganizationConfigAsync(string? projectKey, string organizationId)
        {
            var tenantId = ResolveTenantId(projectKey);
            if (_resourceRepository == null || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(organizationId))
            {
                return null;
            }

            return await _resourceRepository.GetOrganizationConfigAsync(tenantId, organizationId);
        }

        private async Task<ValidationFailure?> ValidateSignUpPolicyAsync(
            CreateUserRequest command,
            User? existingUser,
            string organizationId,
            OrganizationConfig? orgConfig)
        {
            if (command.UserCreationType == UserCreationType.Portal
                && command.UserPassType == UserPassType.Password
                && _identityAccessManagementRepository != null)
            {
                var signUpSetting = await _identityAccessManagementRepository.GetSignUpSettingAsync();
                if (signUpSetting is not null && !signUpSetting.IsEmailPasswordSignUpEnabled)
                {
                    return new ValidationFailure(nameof(CreateUserRequest.UserCreationType), "Email/password signup is disabled by tenant configuration.");
                }
            }

            return await ValidateSignUpPolicyAsync(command.UserCreationType, existingUser, organizationId, orgConfig);
        }

        private static Task<ValidationFailure?> ValidateSignUpPolicyAsync(
            UserCreationType creationType,
            User? existingUser,
            string organizationId,
            OrganizationConfig? orgConfig)
        {
            if (orgConfig == null)
            {
                return Task.FromResult<ValidationFailure?>(null);
            }

            if (!IsOrgCreationAllowed(creationType, orgConfig))
            {
                return Task.FromResult<ValidationFailure?>(new ValidationFailure(nameof(CreateUserRequest.UserCreationType), $"Signup is disabled for organization '{organizationId}' and creation type '{creationType}'."));
            }

            if (!orgConfig.IsMultiOrgEnabled
                && existingUser is not null
                && existingUser.OrganizationIds.Any(id => !string.Equals(id, organizationId, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<ValidationFailure?>(new ValidationFailure(nameof(CreateUserRequest.OrgId), $"Organization '{organizationId}' does not allow multi-organization signup."));
            }

            return Task.FromResult<ValidationFailure?>(null);
        }

        private static bool IsOrgCreationAllowed(UserCreationType creationType, OrganizationConfig orgConfig)
        {
            return creationType switch
            {
                UserCreationType.Portal or UserCreationType.Social => orgConfig.AllowCreationFromCloud,
                UserCreationType.Api or UserCreationType.Service or UserCreationType.ThirdParty => orgConfig.AllowCreationFromConstruct,
                _ => true
            };
        }

        private static List<string> ResolveDefaultRoles(OrganizationConfig? orgConfig)
        {
            return orgConfig?.Roles?
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];
        }

        private string ResolveTenantId(string? projectKey)
        {
            if (!string.IsNullOrWhiteSpace(_blocksContext?.TenantId))
            {
                return _blocksContext.TenantId;
            }

            var contextTenantId = BlocksContext.GetContext()?.TenantId;
            if (!string.IsNullOrWhiteSpace(contextTenantId))
            {
                return contextTenantId;
            }

            return projectKey ?? string.Empty;
        }

        private static List<string> BuildOrganizationIds(
            Dictionary<string, List<string>> roles,
            Dictionary<string, List<string>> permissions,
            string fallbackOrganizationId)
        {
            var orgIds = roles.Keys
                .Concat(permissions.Keys)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orgIds.Count == 0)
            {
                orgIds.Add(string.IsNullOrWhiteSpace(fallbackOrganizationId) ? "default" : fallbackOrganizationId);
            }

            return orgIds;
        }

        public async Task ExecuteUserMutationViaSsoCommandAsync(CreateUserViaSsoEvent command)
        {
            _logger.LogInformation("User Mutation event -- initiate");

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);
            if (command.SendWelcomeMail)
            {
                await SendPostEventAsync(user, command.MailPurpose, command.ProjectKey);
            }
            await SaveUserTimelineAsync(user, command.Action);
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

        private async Task<bool> SendPostEventAsync(User user, string mailPurpose, string projectKey)
        {
            return await _identityAccessManagementService.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);
        }

    }
}

