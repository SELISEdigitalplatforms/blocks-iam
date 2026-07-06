using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Iam.DomainService.Users
{
    public class UserManagementMutationService : IUserManagementMutationService
    {
        private const string DefaultOrganizationId = "default";
        private readonly ILogger<UserManagementMutationService> _logger;
        private readonly IValidator<CreateUserRequest> _createValidator;
        private readonly IValidator<UpdateUserRequest> _updateValidator;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly IUserRepository _userRepository;
        private readonly IIdentityAccessManagementRepository? _identityAccessManagementRepository;
        private readonly IResourceRepository? _resourceRepository;
        private readonly IMessageClient _messageClient;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        public UserManagementMutationService(
            ILogger<UserManagementMutationService> logger,
            IValidator<CreateUserRequest> createValidator,
            IValidator<UpdateUserRequest> updateValidator,
            IIdentityAccessManagementService identityAccessManagementService,
            IUserRepository userRepository,
            IMessageClient messageClient,
            ICacheClient cacheClient,
            ITenants tenants,
            IIdentityAccessManagementRepository? identityAccessManagementRepository = null,
            IResourceRepository? resourceRepository = null,
            IHttpContextAccessor? httpContextAccessor = null
        )
        {
            _logger = logger;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _identityAccessManagementService = identityAccessManagementService;
            _userRepository = userRepository;
            _messageClient = messageClient;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _identityAccessManagementRepository = identityAccessManagementRepository;
            _resourceRepository = resourceRepository;
            _httpContextAccessor = httpContextAccessor;
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

            await SendEvent(itemId, MutationEventType.Create);
            var bc = BlocksContext.GetContext();

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
                    ConsumerName = IdpConstants.IamQueue,
                    Payload = new UserMutationEvent
                    {
                        ItemId = itemId,
                        Action = mutationEventType
                    }
                }
            );
            _logger.LogInformation("User mutation event -- sent");
        }

        public async Task<string> ProcessCreateUserAsync(CreateUserRequest command)
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

            var user = new User
            {
                ItemId = id,
                CreatedDate = DateTime.Now,
                CreatedBy = bc?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = bc?.UserId ?? id,
                Email = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                UserName = (string.IsNullOrWhiteSpace(command.UserName) ? command.Email : command.UserName).ToLower(),
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
                Attributes = command.Attributes ?? new Dictionary<string, object>(),
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

            var organizationId = user.OrganizationIds.FirstOrDefault(x => x == blocksContext?.OrganizationId);

            if (organizationId == null && (blocksContext?.OrganizationId == null || blocksContext?.OrganizationId == DefaultOrganizationId))
            {
                organizationId = DefaultOrganizationId;
            }

            if(organizationId == null)
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

            user.Salutation = command.Salutation ?? string.Empty;
            user.FirstName = command.FirstName ?? string.Empty;
            user.LastName = command.LastName ?? string.Empty;
            user.PhoneNumber = command.PhoneNumber ?? string.Empty;
            user.LastUpdatedDate = DateTime.Now;
            user.LastUpdatedBy = blocksContext?.UserId ?? user.ItemId;
            user.Tags = command.Tags ?? user.Tags;
            user.ProfileImageId = command.ProfileImageId ?? string.Empty;
            user.ProfileImageUrl = command.ProfileImageUrl ?? string.Empty;
            user.MfaEnabled = command.MfaEnabled;

            user.Roles[organizationId] = command.Roles ?? user.Roles.GetValueOrDefault(organizationId, new List<string>());
            user.Permissions[organizationId] = command.Permissions ?? user.Permissions.GetValueOrDefault(organizationId, new List<string>());

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
                ConsumerName = IdpConstants.IamQueue,
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
            var bc = BlocksContext.GetContext();
            var path = $"{(config.IsOidcEnabled ? IamHelper.OidcActivateRoute + bc.TenantId : config.AccountActivationPath)}?code={key}&lang={user.Language}";
            if (!IamHelper.TryBuildUserActionUrl(config, path, out var accountActivationUri, _httpContextAccessor, logger: _logger))
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

        private async Task<bool> SaveUserTimelineAsync(User user, MutationEventType mutationEventType)
        {
            var blocksContext = BlocksContext.GetContext();
            var timeline = new UserTimeline
            {
                ItemId = Guid.NewGuid().ToString(),
                UserId = user.ItemId,
                OrganizationId = blocksContext?.OrganizationId ?? DefaultOrganizationId,
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

            var blocksContext = BlocksContext.GetContext();

            var organizationId = user.OrganizationIds.FirstOrDefault(x => x == blocksContext?.OrganizationId);

            if (organizationId == null && (blocksContext?.OrganizationId == null || blocksContext?.OrganizationId == DefaultOrganizationId))
            {
                organizationId = DefaultOrganizationId;
            }

            if(organizationId == null)
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

            user.Roles[organizationId] = command.Roles ?? user.Roles.GetValueOrDefault(organizationId, new List<string>());
            user.Permissions[organizationId] = command.Permissions ?? user.Permissions.GetValueOrDefault(organizationId, new List<string>());

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

        public async Task<BaseMutationResponse> UpdateOrganizationUserAsync(UpdateOrganizationUserRequest command)
        {
            _logger.LogInformation("UpdateOrganizationUser start");
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();

            if(!tenantConfig.IsMultiOrgEnabled && !string.IsNullOrWhiteSpace(command.OrganizationId))
            {
                _logger.LogInformation("UpdateOrganizationUser end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "OrganizationId", "Multi-organization is not enabled for the tenant" }
                    }
                };
            }

            if(command.OrganizationId == DefaultOrganizationId)
            {
                _logger.LogInformation("UpdateOrganizationUser end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "OrganizationId", "OrganizationId cannot be default" }
                    }
                };
            }

            var user = await _userRepository.GetUserByIdAsync(command.UserId);
            if (user == null)
            {
                _logger.LogInformation("UpdateOrganizationUser end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Not found" }
                    }
                };
            }

            var organization = await _resourceRepository.GetOrganizationById(command.OrganizationId);

            if(organization == null)
            {
                _logger.LogInformation("UpdateOrganizationUser end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "OrganizationId", "Organization not found" }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();

            if(blocksContext?.OrganizationId != command.OrganizationId || blocksContext?.OrganizationId == DefaultOrganizationId)
            {
                _logger.LogInformation("UpdateOrganizationUser end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "OrganizationId", "BlocksContext organization id must match command organization id and cannot be default" }
                    }
                };
            }

            var organizationId = user.OrganizationIds.FirstOrDefault(x => x == command?.OrganizationId);

            var addOrUpdate = organizationId == null ? "add" : "update";

            if(addOrUpdate == "add")
            {
                user.OrganizationIds.Add(command.OrganizationId);
                user.Roles[command.OrganizationId] = command.Roles ?? new List<string> { "user" };
                user.Permissions[command.OrganizationId] = command.Permissions ?? new List<string>();
            }
            else
            {
                user.Roles[command.OrganizationId] = command.Roles ?? user.Roles.GetValueOrDefault(command.OrganizationId, new List<string>());
                user.Permissions[command.OrganizationId] = command.Permissions ?? user.Permissions.GetValueOrDefault(command.OrganizationId, new List<string>());
            }

            var result = await _userRepository.UpdateUserAsync(user);

            if (!result)
            {
                _logger.LogInformation("UpdateOrganizationUser end -- Error");
                return new BaseMutationResponse();
            }

            await SendEvent(user.ItemId, MutationEventType.Update);

            _logger.LogInformation("UpdateOrganizationUser end -- Success");
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

            var command = new CreateUserRequest
            {
                Email = @event.Email,
                UserCreationType = UserCreationType.Service,
                MailPurpose = @event.EventType,
                OrganizationId =@event.OrganizationId ?? DefaultOrganizationId,
                Roles = @event.Roles ?? tenantConfig.DefaultRolesForNewUserOnSignUp ?? new List<string>(),
                Permissions = @event.Permissions ?? tenantConfig.DefaultPermissionsForNewUserOnSignUp ?? new List<string>()
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

            var key = await CreateUserByEmailActivationProcessAsync(user, @event.EventType);

            await SaveUserTimelineAsync(user, MutationEventType.Create);

            await _identityAccessManagementService.SendToQueueAsync(@event.EventQueue, new CreateUserByEmailPostEvent
            {
                Key = key,
                UserId = userId,
                EventType = @event.EventType,
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
                ItemId = Guid.NewGuid().ToString(),
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                MailPurpose = eventType
            });

            return key;
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

            var itemId = await ProcessSsoUserAsync(command);

            _logger.LogInformation("User mutation event -- initiate");
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreateUserViaSsoEvent>
                {
                    ConsumerName = IdpConstants.IamQueue,
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
            
            var user = new User
            {
                ItemId = id,
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId ?? id,
                Email = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                UserName = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
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
                Attributes = command.Attributes ?? new Dictionary<string, object>(),
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

        public async Task ExecuteUserMutationViaSsoCommandAsync(CreateUserViaSsoEvent command)
        {
            _logger.LogInformation("User Mutation event -- initiate");

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);
            if (command.SendWelcomeMail)
            {
                await SendPostEventAsync(user, command.MailPurpose);
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

        private async Task<bool> SendPostEventAsync(User user, string mailPurpose)
        {
            return await _identityAccessManagementService.SendAccountActivationEmailAsync(user, mailPurpose);
        }

    }
}

