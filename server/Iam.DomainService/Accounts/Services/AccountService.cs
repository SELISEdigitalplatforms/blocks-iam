
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Iam.DomainService.Accounts
{
    public class AccountService : IAccountService
    {
        private readonly ILogger<AccountService> _logger;
        private readonly IIdentityAccessManagementRepository _repository;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly IValidator<BaseAccountRequest> _accountValidator;
        private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;
        private readonly IValidator<RecoveryUserRequest> _recoverUserValidator;
        private readonly IUserManagementMutationService _userManagementMutationService;
        private readonly IResourceMutationService _resourceMutationService;
        private readonly ICaptchaService _captchaService;
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public AccountService(
            ILogger<AccountService> logger,
            IIdentityAccessManagementRepository repository,
            IIdentityAccessManagementService identityAccessManagementService,
            ICacheClient cacheClient,
            ITenants tenants,
            IValidator<BaseAccountRequest> accountValidator,
            IValidator<ChangePasswordRequest> changePasswordValidator,
            IValidator<RecoveryUserRequest> recoverUserValidator,
            IUserManagementMutationService userManagementMutationService,
            IResourceMutationService resourceMutationService,
            ICaptchaService captchaService,
            IDbContextProvider dbContextProvider,
            IHttpContextAccessor? httpContextAccessor = null
        )
        {
            _logger = logger;
            _repository = repository;
            _identityAccessManagementService = identityAccessManagementService;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _accountValidator = accountValidator;
            _changePasswordValidator = changePasswordValidator;
            _recoverUserValidator = recoverUserValidator;
            _userManagementMutationService = userManagementMutationService;
            _resourceMutationService = resourceMutationService;
            _captchaService = captchaService;
            _dbContextProvider = dbContextProvider;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<BaseAccountResponse> SignupAccountAsync(SignupUserRequest signupUserRequest)
        {
            var basicValidationError = ValidateSignupRequest(signupUserRequest);
            if (basicValidationError != null)
            {
                return basicValidationError;
            }

            var tenantConfiguration = await _repository.GetTenantConfigurationAsync();

            if (tenantConfiguration == null)
            {
                _logger.LogError("Tenant configuration not found during signup.");
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "signup_configuration", "Signup configuration is not set up. Please contact support." }
                    }
                };
            }

            var policyValidation = await ValidateSignupPolicyAsync(signupUserRequest, tenantConfiguration);
            if (policyValidation != null)
            {
                return policyValidation;
            }

            var captchaValidationError = await ValidateCaptchaAsync(signupUserRequest.CaptchaCode);
            if (captchaValidationError != null)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = captchaValidationError
                };
            }

            var normalizedEmail = signupUserRequest.Email.Trim().ToLowerInvariant();
            var existingHandlingResult = await HandleExistingSignupUserAsync(normalizedEmail);
            if (existingHandlingResult != null)
            {
                return existingHandlingResult;
            }

            // Generate user id first so org creation can receive creator id in the same signup flow.
            var signupUserId = Guid.NewGuid().ToString();

            var organizationId = "default";
            if (signupUserRequest.CreateOrganizationDuringSignup)
            {
                var orgResult = await CreateOrganizationForSignupAsync(signupUserRequest, signupUserId);
                if (!orgResult.IsSuccess)
                {
                    return orgResult;
                }

                organizationId = orgResult.ItemId ?? "default";
            }

            return signupUserRequest.IsSsoSignup
                ? await CreateSsoSignupUserAsync(signupUserRequest, tenantConfiguration, normalizedEmail, signupUserId, organizationId)
                : await CreateEmailSignupUserAsync(signupUserRequest, tenantConfiguration, normalizedEmail, signupUserId, organizationId);
        }

        private static BaseAccountResponse? ValidateSignupRequest(SignupUserRequest signupUserRequest)
        {
            if (signupUserRequest == null || string.IsNullOrWhiteSpace(signupUserRequest.Email))
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Email", "Email is required." }
                    }
                };
            }

            if (signupUserRequest.IsSsoSignup && string.IsNullOrWhiteSpace(signupUserRequest.Provider))
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Provider", "Provider is required for SSO signup." }
                    }
                };
            }

            if (signupUserRequest.CreateOrganizationDuringSignup && string.IsNullOrWhiteSpace(signupUserRequest.OrganizationName))
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "OrganizationName", "Organization name is required when organization creation is enabled." }
                    }
                };
            }

            return null;
        }

        private async Task<BaseAccountResponse?> ValidateSignupPolicyAsync(SignupUserRequest signupUserRequest, TenantConfiguration tenantConfiguration)
        {
            if (signupUserRequest.IsSsoSignup && !tenantConfiguration.IsSSoSignUpEnabled)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "signup_disabled", "sso sign-up is disabled." }
                    }
                };
            }

            if (!signupUserRequest.IsSsoSignup && !tenantConfiguration.IsEmailPasswordSignUpEnabled)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "signup_disabled", "sign-up is disabled." }
                    }
                };
            }

            return null;
        }

        private async Task<BaseAccountResponse?> HandleExistingSignupUserAsync(string normalizedEmail)
        {
            var existingUser = await _repository.GetUserByEmailAsync(normalizedEmail);
            if (existingUser == null)
            {
                return null;
            }

            if (existingUser.Active && existingUser.IsVerified)
            {
                _logger.LogWarning("User already exists with email: {Email}", normalizedEmail);
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "already_signed_up", $"{normalizedEmail} is already registered" }
                    }
                };
            }

            var reactivationSent = await SendReActivationAsync(existingUser);
            return new BaseAccountResponse
            {
                IsSuccess = reactivationSent,
                ItemId = existingUser.ItemId,
                Errors = reactivationSent
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>
                    {
                        { "reactivation_failed", "Failed to send activation email." }
                    }
            };
        }

        private async Task<BaseAccountResponse> CreateOrganizationForSignupAsync(SignupUserRequest signupUserRequest, string creatorUserId)
        {
            var orgRequest = new CreateOrganizationRequest
            {
                Name = signupUserRequest.OrganizationName!,
                Description = signupUserRequest.OrganizationDescription,
                CreatedFrom = CreatedFrom.ConstructSignup
            };

            var orgResponse = await _resourceMutationService.CreateOrganizationAsync(orgRequest, creatorUserId);
            if (!orgResponse.IsSuccess)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = (orgResponse.Errors as Dictionary<string, string>) ?? new Dictionary<string, string>
                    {
                        { "organization_creation_failed", "Organization creation failed during signup." }
                    }
                };
            }

            return new BaseAccountResponse
            {
                IsSuccess = true,
                ItemId = orgResponse.ItemId
            };
        }

        private async Task<BaseAccountResponse> CreateEmailSignupUserAsync(
            SignupUserRequest signupUserRequest,
            TenantConfiguration tenantConfiguration,
            string normalizedEmail,
            string signupUserId,
            string organizationId)
        {
            var createUserRequest = new CreateUserRequest
            {
                UserId = signupUserId,
                Email = normalizedEmail,
                MailPurpose = string.IsNullOrWhiteSpace(signupUserRequest.MailPurpose) ? "AccountActivation" : signupUserRequest.MailPurpose,
                UserCreationType = UserCreationType.Portal,
                UserPassType = UserPassType.Password,
                VerifiedType = UserVerifiedType.None,
                OrganizationId = organizationId,
                Roles = tenantConfiguration.DefaultRolesForNewUserOnSignUp ?? new List<string> { },
                Permissions = tenantConfiguration.DefaultPermissionsForNewUserOnSignUp ?? new List<string> { },
                FirstName = signupUserRequest.FirstName,
                LastName = signupUserRequest.LastName,
                PhoneNumber = signupUserRequest.PhoneNumber
            };

            var result = await _userManagementMutationService.CreateUserAsync(createUserRequest);
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ItemId))
            {
                _logger.LogWarning("Signup user creation failed for email: {Email}", normalizedEmail);
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = result.Errors?.ToDictionary() ?? new Dictionary<string, string>
                    {
                        { "creation_failed", "User creation failed" }
                    }
                };
            }

            var createdUser = await _repository.GetUserByIdAsync(result.ItemId);
            var activationSent = createdUser != null && await SendReActivationAsync(createdUser);

            if (!activationSent)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    ItemId = result.ItemId,
                    Errors = new Dictionary<string, string>
                    {
                        { "activation_email_failed", "User created but activation email could not be sent." }
                    }
                };
            }

            return new BaseAccountResponse
            {
                IsSuccess = true,
                ItemId = result.ItemId,
                Errors = null
            };
        }

        private async Task<BaseAccountResponse> CreateSsoSignupUserAsync(
            SignupUserRequest signupUserRequest,
            TenantConfiguration tenantConfiguration,
            string normalizedEmail,
            string signupUserId,
            string organizationId)
        {
            var ssoRequest = new CreateUserViaSsoRequest
            {
                UserId = signupUserId,
                Email = normalizedEmail,
                Platform = signupUserRequest.Provider!,
                MailPurpose = string.IsNullOrWhiteSpace(signupUserRequest.MailPurpose) ? "AccountActivation" : signupUserRequest.MailPurpose,
                SendWelcomeMail = true,
                UserCreationType = UserCreationType.Social,
                Active = true,
                IsVerified = true,
                ExternalUserId = signupUserRequest.ExternalUserId,
                FirstName = signupUserRequest.FirstName,
                LastName = signupUserRequest.LastName,
                PhoneNumber = signupUserRequest.PhoneNumber,
                Roles = tenantConfiguration.DefaultRolesForNewUserOnSignUp ?? new List<string> { },
                Permissions = tenantConfiguration.DefaultPermissionsForNewUserOnSignUp ?? new List<string> { },
                OrganizationId = organizationId,
                Attributes = signupUserRequest.Attributes
            };

            var result = await _userManagementMutationService.CreateUserFromSsoAsync(ssoRequest);
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ItemId))
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = (Dictionary<string, string>?)result.Errors ?? new Dictionary<string, string>
                    {
                        { "sso_creation_failed", "SSO user creation failed." }
                    }
                };
            }

            return new BaseAccountResponse
            {
                IsSuccess = true,
                ItemId = result.ItemId,
                Errors = null
            };
        }

        private async Task<Dictionary<string, string>?> ValidateCaptchaAsync(string? captchaCode)
        {
            var captchaConfigurationCollection = _dbContextProvider.GetCollection<CaptchaConfiguration>("CaptchaConfigurations");
            var captchaConfiguration = await (await captchaConfigurationCollection
                .FindAsync(Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.IsEnable, true)))
                .FirstOrDefaultAsync();

            if (captchaConfiguration == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(captchaCode))
            {
                return new Dictionary<string, string>
                {
                    { "CaptchaCode", "Captcha verification is required. Please complete the captcha and try again." }
                };
            }

            var verifyCaptchaQueryResponse = await _captchaService.VerifyCaptchaAsync(new VerifyCaptchaRequest
            {
                VerificationCode = captchaCode,
                ConfigurationName = captchaConfiguration.Provider
            });

            if (verifyCaptchaQueryResponse.Verified)
            {
                return null;
            }

            return new Dictionary<string, string>
            {
                { "CaptchaCode", "Captcha verification is required. Please complete the captcha and try again." }
            };
        }

        public async Task<BaseAccountResponse> ActivateAccountAsync(ActivateUserRequest activateUserRequest)
        {
            var validatorResult = await _accountValidator.ValidateAsync(activateUserRequest);
            if (!validatorResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    Errors = validatorResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await ProcessActivationAsync(activateUserRequest);

            return new BaseAccountResponse
            {
                IsSuccess = result
            };

        }

        public async Task<bool> ProcessActivationAsync(ActivateUserRequest activateUserRequest)
        {
            var userId = await _cacheClient.GetStringValueAsync(activateUserRequest.Code);

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {Uid}", userId);
                return false;
            }

            user.Active = true;
            user.IsVerified = true;
            user.FirstName = activateUserRequest.FirstName;
            user.LastName = activateUserRequest.LastName;

            if (!string.IsNullOrWhiteSpace(activateUserRequest.Password))
            {
                var bc = BlocksContext.GetContext();
                var tenantId = bc?.TenantId;
                var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
                user.Password = _identityAccessManagementService.HashPassword(activateUserRequest.Password, tenant?.TenantSalt);
                user.PasswordSetTime = DateTime.Now;
                user.PasswordChangedAtUtc = DateTime.UtcNow;
                user.LastCredentialRotationAtUtc = DateTime.UtcNow;
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                user.TokenVersion += 1;
            }

            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                await _cacheClient.RemoveKeyAsync(activateUserRequest.Code);
                await _identityAccessManagementService.SendToQueueAsync(IdpConstants.IamQueue, new AccountActivityEvent
                {
                    Code = activateUserRequest.Code,
                    UserId = userId,
                    MailPurpose = activateUserRequest.MailPurpose,
                    PreventPostEvent = activateUserRequest.PreventPostEvent,
                    Event = "Activate_Account"
                });
            }


            return result;
        }

        public async Task<BaseAccountResponse> RecoverAccountAsync(RecoveryUserRequest recoveryRequest)
        {
            var validationResult = await _recoverUserValidator.ValidateAsync(recoveryRequest);

            if (!validationResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var user = await _repository.GetUserByEmailAsync(recoveryRequest.Email);

            if (user == null)
            {
                return new BaseAccountResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        {"Email", "Not_Allowed" }
                    }
                };
            }

            var result = await ProcessRecoverAccountAsync(user, recoveryRequest.MailPurpose);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> ProcessRecoverAccountAsync(User user, string emailPurpose)
        {
            var config = await _repository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var path = $"{(config.IsOidcEnabled ? "oidc/recover" : config.RecoverAccountPath)}?code={key}&lang={user.Language}";
            if (!IamHelper.TryBuildUserActionUrl(config, path, out var recoverAccountUrl, _httpContextAccessor, logger: _logger))
            {
                _logger.LogWarning("Recover account URL could not be built for user {UserId}", user.ItemId);
                return false;
            }

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.RecoverAccountUrlLifetimeInMinutes * 60);

            emailPurpose = string.IsNullOrWhiteSpace(emailPurpose) ? "RecoverAccount" : emailPurpose;
            var result = await SendActivationToEmailAsync(user, recoverAccountUrl, emailPurpose);

            await _repository.InsertUserKeyMapAsync(new UserKeyMap
            {
                ItemId = Guid.NewGuid().ToString(),
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.RecoverAccountUrlLifetimeInMinutes),
                Value = recoverAccountUrl,
                MailPurpose = emailPurpose
            });

            _logger.LogInformation("Send Activation for {UserId} is {Send}", user.ItemId, result ? "sent" : "not sent");
            return true;
        }

        public async Task<bool> SendActivationToEmailAsync(User user, string recoverAccountUrl, string emailPurpose)
        {
            _logger.LogInformation("Sending recovery for {Aid} by email: {MPurpose}", user.ItemId, emailPurpose);
            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = new Dictionary<string, string>
                {
                    { "User.DisplayName" , $"{user.FirstName} {user.LastName}"},
                    { "EmailVerification.PageUrl" , recoverAccountUrl}
                },
                Language = user.Language ?? "en-US",
                Purpose = emailPurpose,
                To = new string[] { user.Email.ToLower() }
            };

            return await _identityAccessManagementService.SendEmailAsync(sendMailCommand);
        }

        public async Task<BaseAccountResponse> ResetAccountPasswordAsync(ResetPasswordRequest resetPasswordRequest)
        {
            var validatorResult = await _accountValidator.ValidateAsync(resetPasswordRequest);
            if (!validatorResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    Errors = validatorResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await ProcessResetPasswordAsync(resetPasswordRequest);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> ProcessResetPasswordAsync(ResetPasswordRequest resetPasswordRequest)
        {
            var userId = await _cacheClient.GetStringValueAsync(resetPasswordRequest.Code);

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {AccId}", userId);
                return false;
            }

            var bc = BlocksContext.GetContext();
            var tenantId = bc?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            user.Password = _identityAccessManagementService.HashPassword(resetPasswordRequest.Password, tenant?.TenantSalt);
            user.PasswordSetTime = DateTime.Now;
            user.PasswordChangedAtUtc = DateTime.UtcNow;
            user.LastCredentialRotationAtUtc = DateTime.UtcNow;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.TokenVersion += 1;
            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                await _cacheClient.RemoveKeyAsync(resetPasswordRequest.Code);
                await _identityAccessManagementService.SendToQueueAsync(IdpConstants.IamQueue, new AccountActivityEvent
                {
                    Code = resetPasswordRequest.Code,
                    UserId = userId,
                    Event = "Reset_Password",
                    PreventPostEvent = !resetPasswordRequest.LogoutFromAllDevices
                });
            }


            return result;
        }

        public async Task<BaseAccountResponse> ChangePasswordAsync(ChangePasswordRequest changePasswordRequest)
        {
            var validatorResult = await _changePasswordValidator.ValidateAsync(changePasswordRequest);
            if (!validatorResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    Errors = validatorResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await ProcessChangePasswordAsync(changePasswordRequest);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> ProcessChangePasswordAsync(ChangePasswordRequest changePasswordRequest)
        {
            var bc = BlocksContext.GetContext();

            var user = await _repository.GetUserByIdAsync(bc?.UserId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {Id}", bc?.UserId);
                return false;
            }

            var tenantId = bc?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            var passwordMatched = _identityAccessManagementService.VerifyPassword(changePasswordRequest.OldPassword, user.Password, tenant?.TenantSalt);
            if (!passwordMatched)
            {
                _logger.LogError("Password not matched");
                return false;
            }

            user.Password = _identityAccessManagementService.HashPassword(changePasswordRequest.NewPassword, tenant?.TenantSalt);
            user.PasswordSetTime = DateTime.Now;
            user.PasswordChangedAtUtc = DateTime.UtcNow;
            user.LastCredentialRotationAtUtc = DateTime.UtcNow;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.TokenVersion += 1;
            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                var config = await _repository.GetIamConfigurationAsync();
                await _identityAccessManagementService.SendToQueueAsync(IdpConstants.IamQueue, new AccountActivityEvent
                {
                    UserId = bc?.UserId,
                    Event = "Change_Password",
                    PreventPostEvent = !config.LogoutOnPasswordChange
                });
            }
            return result;
        }

        public async Task<BaseAccountResponse> ResendActivationAsync(ResendActivationRequest resendActivationRequest)
        {
            if (string.IsNullOrWhiteSpace(resendActivationRequest.UserId))
            {
                return new BaseAccountResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "UserId", "UserId_Required" }
                    }
                };
            }

            var user = await _repository.GetUserByIdAsync(resendActivationRequest.UserId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {RuserId}", resendActivationRequest.UserId);
                return new BaseAccountResponse();
            }

            var result = await SendReActivationAsync(user);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> SendReActivationAsync(User user)
        {
            var config = await _repository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var path = $"{(config.IsOidcEnabled ? "oidc/activate" : config.AccountActivationPath)}?code={key}&lang={user.Language}";
            if (!IamHelper.TryBuildUserActionUrl(config, path, out var accountActivationUri, _httpContextAccessor, logger: _logger))
            {
                _logger.LogWarning("Activation URL could not be built for user {UserId}", user.ItemId);
                return false;
            }

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            var emailPurpose = string.IsNullOrWhiteSpace(user.MailPurpose) ? "AccountActivation" : user.MailPurpose;
            var result = await _identityAccessManagementService.SendActivationToEmailAsync(user, accountActivationUri, emailPurpose);

            await _repository.InsertUserKeyMapAsync(new UserKeyMap
            {
                ItemId = Guid.NewGuid().ToString(),
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                Value = accountActivationUri,
                MailPurpose = emailPurpose
            });

            _logger.LogInformation("Send ReActivation for {UsId} is {Send}", user.ItemId, result ? "sent" : "not sent");
            return result;
        }

        public async Task<ActivationCodeValidationResponse> ValidateAccountActivationCodeAsync(ValidateActivationCodeRequest validateActivationCodeRequest)
        {
            if (string.IsNullOrWhiteSpace(validateActivationCodeRequest.ActivationCode))
            {
                return new ActivationCodeValidationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ActivationCode", "ActivationCode_Required" }
                    }
                };
            }

            var isCodeExists = await _cacheClient.KeyExistsAsync(validateActivationCodeRequest.ActivationCode);

            if (isCodeExists)
                return new ActivationCodeValidationResponse { IsSuccess = true };

            var userId = await _repository.GetUserIdFromKeyMapByKeyAsync(validateActivationCodeRequest.ActivationCode);

            return new ActivationCodeValidationResponse
            {
                UserId = userId,
                IsSuccess = !string.IsNullOrWhiteSpace(userId),
                Errors = !string.IsNullOrWhiteSpace(userId) ? null : new Dictionary<string, string>
                {
                    { "ActivationCode", "Invalid_ActivationCode" }
                }
            };
        }

        public async Task<SaveSignUpSettingResponse> SaveSignUpSettingAsync(SaveSignUpSettingRequest request)
        {
            var settings = await _repository.GetTenantConfigurationAsync();

            if (settings == null)
            {
                return new SaveSignUpSettingResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "sign_up_setting_exist", "SignUpSetting already exist" } }
                };
            }

            settings.IsEmailPasswordSignUpEnabled = request.IsEmailPasswordSignUpEnabled;
            settings.IsSSoSignUpEnabled = request.IsSSoSignUpEnabled;
            settings.DefaultPermissionsForNewUserOnSignUp = request.DefaultPermissionsForNewUserOnSignUp;
            settings.DefaultRolesForNewUserOnSignUp = request.DefaultRolesForNewUserOnSignUp;
            settings.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            settings.LastUpdatedDate = DateTime.UtcNow;

            await _repository.SaveSignUpSettingAsync(settings);

            return new SaveSignUpSettingResponse { IsSuccess = true, ItemId = settings.ItemId };
        }

        public async Task<Dictionary<string, object>> GetSignUpSettingAsync()
        {
            var tenantConfiguration = await _repository.GetTenantConfigurationAsync();
            if (tenantConfiguration == null)
            {
                return new Dictionary<string, object>
                {
                    { "IsEmailPasswordSignUpEnabled", false },
                    { "IsSSoSignUpEnabled", false },
                    { "DefaultRolesForNewUser", new List<string>() },
                    { "DefaultPermissionsForNewUser", new List<string>() }
                };
            }

            return new Dictionary<string, object>
            {
                { "IsEmailPasswordSignUpEnabled", tenantConfiguration.IsEmailPasswordSignUpEnabled },
                { "IsSSoSignUpEnabled", tenantConfiguration.IsSSoSignUpEnabled },
                { "DefaultRolesForNewUser", tenantConfiguration.DefaultRolesForNewUserOnSignUp },
                { "DefaultPermissionsForNewUser", tenantConfiguration.DefaultPermissionsForNewUserOnSignUp }
            };
        }

        /// <summary>
        /// Admin method to unlock a locked account.
        /// Resets FailedLoginCount, LockoutUntilUtc, and LockoutCount.
        /// </summary>
        public async Task<BaseAccountResponse> UnlockAccountAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new BaseAccountResponse
                {
                    Errors = new Dictionary<string, string> { { "UserId", "UserId_Required" } }
                };
            }

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found for unlock request: {UserId}", userId);
                return new BaseAccountResponse
                {
                    Errors = new Dictionary<string, string> { { "UserId", "User_Not_Found" } }
                };
            }

            // Reset lockout and failed login counters
            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;
            user.LockoutCount = 0; // Reset exponential backoff counter
            user.LastUpdatedDate = DateTime.UtcNow;
            user.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "system";

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                _logger.LogInformation("Account unlocked by admin for user: {UserId}", userId);

                // Send notification email to user
                try
                {
                    await SendAccountUnlockedNotificationAsync(user);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send account unlock notification for user: {UserId}", userId);
                    // Don't fail the unlock operation if email fails
                }
            }

            return new BaseAccountResponse { IsSuccess = result };
        }

        /// <summary>
        /// Sends email notification when account is locked due to failed login attempts.
        /// </summary>
        public async Task SendAccountLockedNotificationAsync(User user, DateTime lockoutUntilUtc)
        {
            try
            {
                var sendMailCommand = new SendMail
                {
                    Cc = Array.Empty<string>(),
                    Bcc = Array.Empty<string>(),
                    BodyDataContext = new Dictionary<string, string>
                    {
                        { "User.DisplayName", $"{user.FirstName} {user.LastName}" },
                        { "LockoutUntil", lockoutUntilUtc.ToString("g") },
                        { "LockoutReason", "Too many failed login attempts" }
                    },
                    Language = user.Language ?? "en-US",
                    Purpose = "AccountLockedNotification",
                    To = new[] { user.Email?.ToLower() ?? string.Empty }
                };

                var recipientEmail = sendMailCommand.To.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(recipientEmail))
                {
                    await _identityAccessManagementService.SendEmailAsync(sendMailCommand);
                    _logger.LogInformation("Account locked notification sent to user: {UserId}", user.ItemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send account locked notification for user: {UserId}", user.ItemId);
            }
        }

        /// <summary>
        /// Sends email notification when account is unlocked by admin.
        /// </summary>
        public async Task SendAccountUnlockedNotificationAsync(User user)
        {
            try
            {
                var sendMailCommand = new SendMail
                {
                    Cc = Array.Empty<string>(),
                    Bcc = Array.Empty<string>(),
                    BodyDataContext = new Dictionary<string, string>
                    {
                        { "User.DisplayName", $"{user.FirstName} {user.LastName}" },
                        { "UnlockTime", DateTime.UtcNow.ToString("g") }
                    },
                    Language = user.Language ?? "en-US",
                    Purpose = "AccountUnlockedNotification",
                    To = new[] { user.Email?.ToLower() ?? string.Empty }
                };

                var recipientEmail = sendMailCommand.To.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(recipientEmail))
                {
                    await _identityAccessManagementService.SendEmailAsync(sendMailCommand);
                    _logger.LogInformation("Account unlocked notification sent to user: {UserId}", user.ItemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send account unlocked notification for user: {UserId}", user.ItemId);
            }
        }
    }
}