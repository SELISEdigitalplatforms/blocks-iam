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
using Iam.DomainService.Shared.Entities;

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
            _tenants = tenants;
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
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<UpdateResourceUsageCommand>
            {
                ConsumerName = Constants.IdentifierQueue,
                Payload = new UpdateResourceUsageCommand
                {
                    Resource = "blocks-idp::createuser",
                    TenantId = bc.TenantId,
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

            // var signUpPolicyError = await ValidateSignUpPolicyAsync(command, user, effectiveOrganizationId, orgConfig);

            // if (signUpPolicyError != null)
            // {
            //     throw new ValidationException([signUpPolicyError]);
            // }
            if(command.Roles == null || command.Roles.Count == 0)
            {
                command.Roles = organization != null && organization.DefaultRoleForMembers != null && organization.DefaultRoleForMembers.Count > 0
                    ? organization.DefaultRoleForMembers
                    : new List<string> { "user" };
            }
            
            if(command.Permissions == null)
            {
                command.Permissions = new List<string>();
            }

            var user = MapUser(command);
            await _userRepository.CreateUserAsync(user);

            return user.ItemId;
        }

        public User MapUser(CreateUserRequest command)
        {
            var id = Guid.NewGuid().ToString();
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
                OrganizationIds = new List<string> { command.OrganizationId },
                Roles = new Dictionary<string, List<string>> { [command.OrganizationId] = command.Roles ?? new List<string> { "user" } },
                Permissions = new Dictionary<string, List<string>> { [command.OrganizationId] = command.Permissions ?? new List<string>() },
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
                ExternalIdentities = new List<ExternalIdentity>()
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

        // public async Task<bool> CreateUserByEmailAsync(CreateUserByEmailEvent @event)
        // {
        //     _logger.LogInformation("User creation start from CreateUserByEmail");

        //     var command = new CreateUserRequest
        //     {
        //         Email = @event.Email,
        //         UserCreationType = UserCreationType.Service,
        //         MailPurpose = @event.EventType,
        //         OrgId = DefaultOrganizationId,
        //         Roles = new Dictionary<string, List<string>> { [DefaultOrganizationId] = ["user"] },
        //         Permissions = new Dictionary<string, List<string>> { [DefaultOrganizationId] = [] }
        //     };

        //     _blocksContext = BlocksContext.GetContext();

        //     var validationResult = await _createValidator.ValidateAsync(command);
        //     if (!validationResult.IsValid)
        //     {
        //         _logger.LogInformation("User creation end -- Validation Error -- CreateUserByEmail");
        //         return false;
        //     }

        //     string itemId;
        //     try
        //     {
        //         itemId = await ProcessAsync(command);
        //     }
        //     catch (ValidationException)
        //     {
        //         _logger.LogInformation("User creation end -- Signup Policy Validation Error -- CreateUserByEmail");
        //         return false;
        //     }

        //     await ProcessCreateUserByEmailAfterActionAsync(@event, itemId);

        //     _logger.LogInformation("User creation end -- Success -- CreateUserByEmail");
        //     return true;
        // }

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

        public async Task<BaseMutationResponse> CreateUserFromSsoAsync(CreateUserViaSsoRequest command)
        {
            _logger.LogInformation("User creation start");

            blocksContext = BlocksContext.GetContext();

            var fallbackOrganizationId = BlocksContext.GetContext()?.OrganizationId ?? DefaultOrganizationId;
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
            var fallbackOrganizationId = BlocksContext.GetContext()?.OrganizationId ?? DefaultOrganizationId;
            var normalizedRoles = NormalizeOrgClaimMap(command.Roles, fallbackOrganizationId, defaultRoles.Count > 0 ? defaultRoles : ["user"]);
            var normalizedPermissions = NormalizeOrgClaimMap(command.Permissions, fallbackOrganizationId, []);
            var tenantId = BlocksContext.GetContext()?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            
            var user = new User
            {
                ItemId = id,
                CreatedDate = DateTime.Now,
                CreatedBy = _blocksContext?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = _blocksContext?.UserId ?? id,
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
                Roles = normalizedRoles,
                Permissions = normalizedPermissions,
                OrganizationIds = BuildOrganizationIds(normalizedRoles, normalizedPermissions, fallbackOrganizationId),
                UserCreationType = command.UserCreationType,
                UserPassType = UserPassType.None,
                Tags = [],
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
            Dictionary<string, List<string>> permissions,
            string? contextOrganizationId = null)
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

            if (!string.IsNullOrWhiteSpace(contextOrganizationId))
            {
                return contextOrganizationId;
            }

            return DefaultOrganizationId;
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

        private async Task<List<string>> DerivePermissionsFromRolesAsync(string organizationId, List<string> roleSlugs)
        {
            if (roleSlugs is null || roleSlugs.Count == 0)
                return [];

            var derivedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Get all permissions from the repository (TODO: optimize with org-scoped query if available)
                var allPermissions = await _resourceRepository.GetPermissionsAsync(
                    new GetPermissionsRequest { Page = 0, PageSize = 10000 }
                );

                if (allPermissions.Item1 == null)
                    return [];

                var permissionList = allPermissions.Item1.ToList();

                // For each permission, check if any of the user's role slugs match
                foreach (var permission in permissionList)
                {
                    if (permission.Roles == null || !permission.Roles.TryGetValue(organizationId, out var rolesForPermission))
                        continue;

                    // Check if any user role slug is in this permission's role list for the org
                    var hasMatchingRole = roleSlugs.Any(slug => 
                        rolesForPermission.Any(permRole => 
                            string.Equals(permRole, slug, StringComparison.OrdinalIgnoreCase)
                        )
                    );

                    if (hasMatchingRole)
                    {
                        // Add permission name (or ItemId) to derived permissions
                        derivedPermissions.Add(permission.Name ?? permission.ItemId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deriving permissions from roles for org {OrgId}", organizationId);
            }

            return derivedPermissions.ToList();
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

