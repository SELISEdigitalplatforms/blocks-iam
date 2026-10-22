
#pragma warning disable CA1303

using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.DelegatedAdministration;
using Iam.DomainService.Utilities;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Shared.Serialization;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// Constants used to identify account activity event types. Centralised so the
    /// string literals are not duplicated across the service and its consumers.
    /// </summary>
    internal static class AccountEvents
    {
        public const string ActivateAccount = "Activate_Account";
        public const string ResetPassword = "Reset_Password";
        public const string ChangePassword = "Change_Password";
    }

    /// <summary>
    /// Defaults applied when an email purpose is not supplied by the caller.
    /// </summary>
    internal static class AccountMailPurposes
    {
        public const string AccountActivation = "AccountActivation";
        public const string RecoverAccount = "RecoverAccount";
    }

    /// <summary>
    /// Application service implementing the public account lifecycle:
    /// signup, activation, recovery, password reset/change, reactivation,
    /// activation-code validation, signup settings, admin unlock and
    /// account locked/unlocked notifications.
    /// </summary>
    public class AccountService : IAccountService
    {
        private const string DefaultOrganizationId = "default";

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
        private readonly ICaptchaConfigurationService _captchaConfigurationService;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly IDefaultOidcClientResolver? _defaultOidcClientResolver;
        private readonly IUserSessionTerminator? _userSessionTerminator;
        private readonly IFailedLoginTracker? _failedLoginTracker;
        private readonly IDelegatedAdministrationService? _delegatedAdministration;
        private readonly IValidator<SignupOrganizationInfo>? _signupOrganizationValidator;
        private readonly IOrganizationNameResolver? _organizationNameResolver;
        private readonly IConfiguration? _configuration;
        private readonly ISignupOrganizationResolver _signupOrganizationResolver;

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
            ICaptchaConfigurationService captchaConfigurationService,
            IUserActivityDispatcher userActivityDispatcher,
            // Required, unlike the optional trailing dependencies: without it a sign-up cannot be placed.
            ISignupOrganizationResolver signupOrganizationResolver,
            IHttpContextAccessor? httpContextAccessor = null,
            IDefaultOidcClientResolver? defaultOidcClientResolver = null,
            // Trailing and optional, matching the two above, so the many existing call sites and
            // test fixtures that construct this service keep compiling. DI supplies both.
            IValidator<SignupOrganizationInfo>? signupOrganizationValidator = null,
            IOrganizationNameResolver? organizationNameResolver = null,
            IConfiguration? configuration = null,
            IUserSessionTerminator? userSessionTerminator = null,
            IFailedLoginTracker? failedLoginTracker = null,
            IDelegatedAdministrationService? delegatedAdministration = null)
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
            _captchaConfigurationService = captchaConfigurationService;
            _userActivityDispatcher = userActivityDispatcher;
            _signupOrganizationResolver = signupOrganizationResolver;
            _httpContextAccessor = httpContextAccessor;
            _defaultOidcClientResolver = defaultOidcClientResolver;
            _signupOrganizationValidator = signupOrganizationValidator;
            _organizationNameResolver = organizationNameResolver;
            _configuration = configuration;
            _userSessionTerminator = userSessionTerminator;
            _failedLoginTracker = failedLoginTracker;
            _delegatedAdministration = delegatedAdministration;
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

            // Organization checks run for every email, before the account lookup, so a refusal
            // (name taken, creation turned off, bad profile, auto-join organization unavailable) cannot
            // tell whether the email has an account.
            if (signupUserRequest.CreateOrganizationDuringSignup)
            {
                var organizationRefusal = await CheckSignupOrganizationAsync(signupUserRequest);
                if (organizationRefusal != null)
                {
                    return organizationRefusal;
                }
            }

            var placement = await ResolveSignupPlacementAsync(signupUserRequest, tenantConfiguration);
            if (placement.Outcome == SignupOrganizationOutcome.TargetUnavailable)
            {
                // Nothing has been written yet: no user, no organization, no mail.
                _logger.LogWarning(
                    "Signup refused: the auto-join organization {OrganizationId} is unavailable",
                    tenantConfiguration.SignupAutoJoinOrganizationId ?? "(none)");
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { IdpConstants.SignupOrganizationUnavailableError, IdpConstants.SignupOrganizationUnavailableMessage }
                    }
                };
            }

            var existingHandlingResult = await HandleExistingSignupUserAsync(signupUserRequest, tenantConfiguration, normalizedEmail);
            if (existingHandlingResult != null)
            {
                return existingHandlingResult;
            }

            return await CreateSignupUserWithPlacementAsync(signupUserRequest, tenantConfiguration, normalizedEmail, placement);
        }

        private async Task<BaseAccountResponse> CreateSignupUserWithPlacementAsync(
            SignupUserRequest signupUserRequest,
            IamTenantConfiguration tenantConfiguration,
            string normalizedEmail,
            SignupOrganizationDecision placement)
        {
            // Generate user id first so org creation can receive creator id in the same signup flow.
            var signupUserId = Guid.NewGuid().ToString();

            // Null means "no organization" (NoOrganization); otherwise "default", the auto-join target, or the
            // organization created just below.
            var organizationId = placement.OrganizationId;

            // Only an organization this sign-up created is ever rolled back. The auto-join target and "default"
            // are shared and must survive a failed sign-up.
            string? createdOrganizationId = null;
            if (placement.Outcome == SignupOrganizationOutcome.CreateOwn)
            {
                var orgResult = await CreateOrganizationForSignupAsync(signupUserRequest, signupUserId);
                if (!orgResult.IsSuccess)
                {
                    return orgResult;
                }

                organizationId = orgResult.ItemId ?? DefaultOrganizationId;
                createdOrganizationId = organizationId == DefaultOrganizationId ? null : organizationId;
            }

            var result = signupUserRequest.IsSsoSignup
                ? await CreateSsoSignupUserAsync(signupUserRequest, tenantConfiguration, normalizedEmail, signupUserId, organizationId ?? DefaultOrganizationId)
                : await CreateEmailSignupUserAsync(signupUserRequest, tenantConfiguration, normalizedEmail, signupUserId, organizationId);

            // Compensate a failed user creation: without this the org survives with no
            // members and permanently reserves the name, so the user's retry fails with
            // "name_already_exists" against their own abandoned org.
            if (!result.IsSuccess && createdOrganizationId != null)
            {
                await RollbackSignupOrganizationAsync(createdOrganizationId, normalizedEmail);
            }

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "Signup in tenant {TenantId} placed user {UserId} by {Outcome} into {OrganizationId}",
                    BlocksContext.GetContext()?.TenantId,
                    signupUserId,
                    placement.Outcome,
                    organizationId ?? "none");
            }

            return result.IsSuccess ? SignupAccepted() : result;
        }

        /// <summary>
        /// The one success answer signup gives, for a new email and for one that already has an
        /// account alike: no user id, no organization id, no hint of which case it was. What
        /// happens next arrives by mail (activation, password reset, or nothing for a blocked account).
        /// </summary>
        private static BaseAccountResponse SignupAccepted() => new()
        {
            IsSuccess = true,
            Errors = new Dictionary<string, string>()
        };

        /// <summary>
        /// Everything that can refuse the requested organization before it is created: the profile
        /// shape, and the tenant's creation rules and name uniqueness (same checks
        /// <c>CreateOrganizationAsync</c> runs). Null when it would be accepted.
        /// </summary>
        private async Task<BaseAccountResponse?> CheckSignupOrganizationAsync(SignupUserRequest signupUserRequest)
        {
            var organizationName = signupUserRequest.ResolveOrganizationName()!;
            var validationErrors = await ValidateSignupOrganizationAsync(signupUserRequest, organizationName);
            if (validationErrors != null)
            {
                return new BaseAccountResponse { IsSuccess = false, Errors = validationErrors };
            }

            var refusal = await _resourceMutationService.CheckOrganizationCreationAsync(
                BuildSignupOrganizationRequest(signupUserRequest, organizationName));
            return refusal == null ? null : await OrganizationRefusedAsync(refusal, organizationName);
        }

        private async Task<BaseAccountResponse> OrganizationRefusedAsync(BaseMutationResponse orgResponse, string organizationName)
        {
            var errors = (orgResponse.Errors as Dictionary<string, string>) ?? new Dictionary<string, string>
            {
                { "organization_creation_failed", "Organization creation failed during signup." }
            };

            return new BaseAccountResponse
            {
                IsSuccess = false,
                Errors = errors,
                // Only for a name clash, and only as a convenience: the caller gets usable
                // alternatives instead of sending the user back to invent another name.
                OrganizationNameSuggestions = errors.ContainsKey("name_already_exists")
                    ? await SuggestOrganizationNamesAsync(organizationName)
                    : new List<string>()
            };
        }

        /// <summary>
        /// Where a new email sign-up lands, decided by the shared <see cref="ISignupOrganizationResolver"/> so
        /// the email and SSO routes cannot drift apart.
        /// <para>
        /// The <c>IsSsoSignup</c> branch is unreachable -- <c>POST auth/signup</c> forces it off -- and keeps its
        /// original placement: <c>default</c>, or the organization the caller asked to create.
        /// </para>
        /// </summary>
        private async Task<SignupOrganizationDecision> ResolveSignupPlacementAsync(
            SignupUserRequest signupUserRequest,
            IamTenantConfiguration tenantConfiguration)
        {
            if (signupUserRequest.IsSsoSignup)
            {
                return signupUserRequest.CreateOrganizationDuringSignup
                    ? new SignupOrganizationDecision(SignupOrganizationOutcome.CreateOwn)
                    : new SignupOrganizationDecision(SignupOrganizationOutcome.Default, DefaultOrganizationId);
            }

            return await _signupOrganizationResolver.ResolveAsync(
                tenantConfiguration,
                SignupRoute.Email,
                signupUserRequest.CreateOrganizationDuringSignup);
        }

        private async Task RollbackSignupOrganizationAsync(string organizationId, string email)
        {
            try
            {
                await _resourceMutationService.DeleteOrganizationAsync(organizationId);
                _logger.LogInformation(
                    "Rolled back organization {OrganizationId} after failed signup for {Email}",
                    organizationId,
                    email);
            }
            catch (Exception ex)
            {
                // The signup already failed; a failed cleanup must not change what the
                // caller sees, so log and move on.
                _logger.LogError(
                    ex,
                    "Failed to roll back organization {OrganizationId} after failed signup for {Email}",
                    organizationId,
                    email);
            }
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

            // Reads the nested organization object as well as the legacy flat field, so a caller
            // that only sends organization.name still satisfies the requirement.
            if (signupUserRequest.CreateOrganizationDuringSignup && signupUserRequest.ResolveOrganizationName() == null)
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

        /// <summary>
        /// Signup for an email that already has an account answers exactly like a new signup
        /// (see <see cref="SignupAccepted"/>); only the mail differs. Active account: the normal
        /// password-reset mail. Not yet activated: the activation mail again. Blocked by an
        /// administrator (<see cref="AccountCodes.IsBlocked"/>): no mail. A failure while sending is
        /// logged, never shown, so it cannot reveal the account either. Null when there is no account.
        /// </summary>
        private async Task<BaseAccountResponse?> HandleExistingSignupUserAsync(SignupUserRequest signupUserRequest, TenantConfiguration tenantConfiguration, string normalizedEmail)
        {
            var existingUser = await _repository.GetUserByEmailAsync(normalizedEmail);
            if (existingUser == null)
            {
                return null;
            }

            if (AccountCodes.IsBlocked(existingUser))
            {
                _logger.LogInformation("Signup for a blocked account {UserId}: no mail sent", existingUser.ItemId);
                return SignupAccepted();
            }

            if (signupUserRequest.IsSsoSignup
                && !string.IsNullOrWhiteSpace(signupUserRequest.Provider)
                && !string.IsNullOrWhiteSpace(signupUserRequest.ExternalUserId))
            {
                var linkResult = await _userManagementMutationService.ActivateAndLinkSocialIdentityAsync(
                    new ActivateAndLinkSocialIdentityRequest
                    {
                        UserId = existingUser.ItemId,
                        Provider = signupUserRequest.Provider,
                        ProviderUserId = signupUserRequest.ExternalUserId,
                        Issuer = signupUserRequest.Provider
                    });

                if (linkResult.IsSuccess)
                {
                    return SignupAccepted();
                }

                _logger.LogWarning("ActivateAndLink failed for {Email}: {Errors}", normalizedEmail, string.Join(",", linkResult.Errors?.Keys ?? Enumerable.Empty<string>()));
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = (linkResult.Errors as Dictionary<string, string>) ?? new Dictionary<string, string>
                    {
                        { "social_link_failed", "Could not link social identity to the existing account." }
                    }
                };
            }

            try
            {
                if (existingUser.Active && existingUser.IsVerified)
                {
                    _logger.LogInformation("Signup for an active account {UserId}: password-reset mail sent", existingUser.ItemId);
                    await ProcessRecoverAccountAsync(
                        existingUser,
                        AccountMailPurposes.RecoverAccount,
                        signupUserRequest.ClientId,
                        signupUserRequest.RedirectUri);
                    return SignupAccepted();
                }

                // The account exists but was never activated, so this is effectively a repeat
                // signup. Honour the organization the user asked for here too, otherwise the
                // name they typed is silently discarded and they activate into "default".
                if (signupUserRequest.CreateOrganizationDuringSignup)
                {
                    var (pendingOrgFailure, _) = await AttachOrganizationToPendingUserAsync(
                        signupUserRequest,
                        tenantConfiguration,
                        existingUser);

                    // The name and the creation rules were checked before the lookup, so this is a
                    // race or an outage: the activation mail still goes out.
                    if (pendingOrgFailure != null)
                    {
                        _logger.LogWarning(
                            "Organization for pending account {UserId} not created: {Errors}",
                            existingUser.ItemId,
                            string.Join(",", pendingOrgFailure.Errors?.Keys ?? Enumerable.Empty<string>()));
                    }
                }

                // Direct call, not a queue hop: the signup request's OIDC context is still in
                // hand here, so a resend returns the user to the same application.
                var reactivationSent = await SendReActivationAsync(
                    existingUser,
                    signupUserRequest.ClientId,
                    signupUserRequest.RedirectUri);
                if (!reactivationSent)
                {
                    _logger.LogWarning("Signup for a pending account {UserId}: activation mail not sent", existingUser.ItemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signup mail for existing account {UserId} failed", existingUser.ItemId);
            }

            return SignupAccepted();
        }

        /// <summary>
        /// Creates the requested organization for an existing but unactivated account and
        /// moves that account into it. Returns a response only on failure; null means the
        /// caller should carry on with the normal reactivation path.
        /// </summary>
        private async Task<(BaseAccountResponse? Failure, string? OrganizationId)> AttachOrganizationToPendingUserAsync(
            SignupUserRequest signupUserRequest,
            TenantConfiguration tenantConfiguration,
            User existingUser)
        {
            // A previous attempt already placed them in a real org; creating a second one
            // would orphan the first and burn another name.
            var existingOrganizationId = existingUser.OrganizationIds?
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && id != DefaultOrganizationId);

            if (!string.IsNullOrWhiteSpace(existingOrganizationId))
            {
                // Report the org they are already in; the profile on this request is not
                // applied to it — signup creates organizations, it never updates them.
                return (null, existingOrganizationId);
            }

            var orgResult = await CreateOrganizationForSignupAsync(signupUserRequest, existingUser.ItemId);
            if (!orgResult.IsSuccess)
            {
                return (orgResult, null);
            }

            var organizationId = orgResult.ItemId;
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return (null, null);
            }

            var roles = tenantConfiguration.DefaultRolesForNewUserOnSignUp ?? new List<string>();
            var permissions = tenantConfiguration.DefaultPermissionsForNewUserOnSignUp ?? new List<string>();

            existingUser.OrganizationIds = new List<string> { organizationId };
            existingUser.Roles = new Dictionary<string, List<string>> { [organizationId] = roles };
            existingUser.Permissions = new Dictionary<string, List<string>> { [organizationId] = permissions };
            existingUser.LastUpdatedDate = DateTime.UtcNow;
            existingUser.LastUpdatedBy = existingUser.ItemId;

            var updated = await _repository.UpdateUserAsync(existingUser);
            if (!updated)
            {
                await RollbackSignupOrganizationAsync(organizationId, existingUser.Email ?? string.Empty);
                return (new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "organization_creation_failed", "Organization creation failed during signup." }
                    }
                }, null);
            }

            return (null, organizationId);
        }

        private async Task<BaseAccountResponse> CreateOrganizationForSignupAsync(SignupUserRequest signupUserRequest, string creatorUserId)
        {
            var organizationName = signupUserRequest.ResolveOrganizationName()!;

            var validationErrors = await ValidateSignupOrganizationAsync(signupUserRequest, organizationName);
            if (validationErrors != null)
            {
                return new BaseAccountResponse { IsSuccess = false, Errors = validationErrors };
            }

            var orgRequest = BuildSignupOrganizationRequest(signupUserRequest, organizationName);

            var orgResponse = await _resourceMutationService.CreateOrganizationAsync(orgRequest, creatorUserId);
            if (!orgResponse.IsSuccess)
            {
                return await OrganizationRefusedAsync(orgResponse, organizationName);
            }

            return new BaseAccountResponse
            {
                IsSuccess = true,
                ItemId = orgResponse.ItemId
            };
        }

        /// <summary>
        /// Shape/size checks on the optional organization profile. Returns null when there is
        /// nothing to validate or everything passed. Name availability is deliberately not
        /// checked here — <c>CreateOrganizationAsync</c> owns that, without a check-then-act gap.
        /// </summary>
        private async Task<Dictionary<string, string>?> ValidateSignupOrganizationAsync(
            SignupUserRequest signupUserRequest,
            string organizationName)
        {
            if (signupUserRequest.Organization == null || _signupOrganizationValidator == null)
            {
                return null;
            }

            // Validate the name that will actually be used, which may have come from the legacy
            // flat field rather than the nested object.
            var toValidate = signupUserRequest.Organization;
            toValidate.Name = organizationName;

            var result = await _signupOrganizationValidator.ValidateAsync(toValidate);
            if (result.IsValid)
            {
                return null;
            }

            return result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.First().ErrorMessage);
        }

        private async Task<List<string>> SuggestOrganizationNamesAsync(string organizationName)
        {
            if (_organizationNameResolver == null)
            {
                return new List<string>();
            }

            try
            {
                return await _organizationNameResolver.SuggestAvailableNamesAsync(organizationName);
            }
            catch (Exception ex)
            {
                // Suggestions are a nicety layered on top of an error that is already being
                // returned; failing to produce them must not change what the caller sees.
                _logger.LogWarning(ex, "Could not generate organization name suggestions for {OrganizationName}", organizationName);
                return new List<string>();
            }
        }

        /// <summary>
        /// Maps the allowlisted signup profile onto a create request, field by field.
        /// <para>
        /// Deliberately explicit rather than a spread or an object-mapper: signup is anonymous,
        /// and <c>CreateOrganizationRequest</c> also carries <c>DefaultRoleForMembers</c> /
        /// <c>DefaultPermissionsForMembers</c>, which new members inherit. Copying wholesale —
        /// now or after someone adds a field later — would hand an unauthenticated caller the
        /// ability to set the roles every member of their new organization receives.
        /// </para>
        /// </summary>
        private static CreateOrganizationRequest BuildSignupOrganizationRequest(
            SignupUserRequest signupUserRequest,
            string organizationName)
        {
            var request = new CreateOrganizationRequest
            {
                Name = organizationName,
                Description = signupUserRequest.ResolveOrganizationDescription(),
                CreatedFrom = CreatedFrom.ConstructSignup
            };

            var profile = signupUserRequest.Organization;
            if (profile == null)
            {
                // No nested object: honour the top-level attributes bag so the legacy shape can
                // still carry extras.
                request.Attributes = AttributeNormalizer.Normalize(signupUserRequest.Attributes, AttributePolicy.Public);
                return request;
            }

            request.Email = profile.Email;
            request.PhoneNumber = profile.PhoneNumber;
            request.WebsiteUrl = profile.WebsiteUrl;
            request.Addresses = profile.Addresses ?? new List<Address>();
            request.Theme = profile.Theme;
            request.LogoUrl = profile.LogoUrl;
            request.Industry = profile.Industry;
            request.TimeZone = profile.TimeZone;
            request.Currency = profile.Currency;
            request.DateFormat = profile.DateFormat;
            request.TimeFormat = profile.TimeFormat;
            request.Locale = profile.Locale;

            // organization.attributes wins; the top-level bag is the legacy fallback.
            request.Attributes = AttributeNormalizer.Normalize(
                profile.Attributes is { Count: > 0 } ? profile.Attributes : signupUserRequest.Attributes,
                AttributePolicy.Public);

            return request;
        }

        /// <param name="organizationId">The organization to join, or null for a user with no organization.</param>
        private async Task<BaseAccountResponse> CreateEmailSignupUserAsync(
            SignupUserRequest signupUserRequest,
            TenantConfiguration tenantConfiguration,
            string normalizedEmail,
            string signupUserId,
            string? organizationId)
        {
            var createUserRequest = new CreateUserRequest
            {
                UserId = signupUserId,
                Email = normalizedEmail,
                MailPurpose = string.IsNullOrWhiteSpace(signupUserRequest.MailPurpose) ? AccountMailPurposes.AccountActivation : signupUserRequest.MailPurpose,
                UserCreationType = UserCreationType.Portal,
                UserPassType = UserPassType.Password,
                VerifiedType = UserVerifiedType.None,
                OrganizationId = organizationId,
                Roles = tenantConfiguration.DefaultRolesForNewUserOnSignUp ?? new List<string> { },
                Permissions = tenantConfiguration.DefaultPermissionsForNewUserOnSignUp ?? new List<string> { },
                FirstName = signupUserRequest.FirstName,
                LastName = signupUserRequest.LastName,
                PhoneNumber = signupUserRequest.PhoneNumber,
                // Rides the user-mutation event through to the activation email builder.
                ClientId = signupUserRequest.ClientId,
                RedirectUri = signupUserRequest.RedirectUri
            };

            var result = organizationId == null
                ? await _userManagementMutationService.CreateSignupUserWithoutOrganizationAsync(createUserRequest)
                : await _userManagementMutationService.CreateUserAsync(createUserRequest);
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
                MailPurpose = string.IsNullOrWhiteSpace(signupUserRequest.MailPurpose) ? AccountMailPurposes.AccountActivation : signupUserRequest.MailPurpose,
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
                // Signup is anonymous, so the user bag gets the same treatment the organization
                // bag above already had. Passing it raw wrote { "_t": "JsonElement" } instead.
                Attributes = AttributeNormalizer.Normalize(signupUserRequest.Attributes, AttributePolicy.Public)
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
            // Reads through the driver, which prefers the blocks-os key/value store and falls back
            // to the legacy secret document. Previously this queried CaptchaConfigurations, a
            // collection nothing writes any more, so signup silently skipped every captcha.
            var captchaConfiguration = await _captchaConfigurationService.GetCaptchaConfigurationAsync();

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
            var userId = await AccountCodes.ResolveAsync(_cacheClient, AccountCodes.Activation, activateUserRequest.Code);
            if (userId == null)
            {
                _logger.LogWarning("Activation refused: the code is not a live activation code");
                return false;
            }

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {Uid}", userId);
                return false;
            }

            if (AccountCodes.IsBlocked(user))
            {
                _logger.LogWarning("Activation refused for blocked user {Uid}", userId);
                return false;
            }

            user.Active = true;
            user.IsVerified = true;
            user.Status = UserLifecycleStatus.Active;
            user.StatusReason = "email_verified";
            // The user spends their own key, so they are the actor.
            user.ActivatedAtUtc = DateTime.UtcNow;
            user.ActivatedBy = user.ItemId;

            // An invited user has no name until they set one here. Only fill an empty name so a
            // user who already had one (e.g. normal signup) is never overwritten on activation.
            if (string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(activateUserRequest.FirstName))
                user.FirstName = activateUserRequest.FirstName.Trim();
            if (string.IsNullOrWhiteSpace(user.LastName) && !string.IsNullOrWhiteSpace(activateUserRequest.LastName))
                user.LastName = activateUserRequest.LastName.Trim();

            if (!string.IsNullOrWhiteSpace(activateUserRequest.Password))
            {
                ApplyPasswordRotation(user, activateUserRequest.Password);
            }

            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                await _cacheClient.RemoveKeyAsync(activateUserRequest.Code);
                await InvalidateActivationCacheAsync(userId, activateUserRequest.Code);
                await _userActivityDispatcher.SendUserActivityAsync(AccountActivation.ActivatedEvent(userId, "iam-account"));

                if (!activateUserRequest.PreventPostEvent)
                {
                    await _identityAccessManagementService.SendAccountActivationEmailAsync(user, activateUserRequest.MailPurpose!);
                }
            }


            return result;
        }

        private async Task InvalidateActivationCacheAsync(string userId, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            await AccountActivation.RetireActivationKeysAsync(_repository, _cacheClient, userId);
        }

        /// <summary>
        /// Applies the standard password-rotation side-effects to a user record:
        /// re-hash the password with the tenant salt, set the rotation timestamps,
        /// rotate the security stamp, increment the token version and clear the
        /// failed-login / lockout counters. Centralised to keep
        /// <see cref="ProcessActivationAsync"/>, <see cref="ProcessResetPasswordAsync"/>
        /// and <see cref="ProcessChangePasswordAsync"/> behaviourally identical.
        /// </summary>
        private void ApplyPasswordRotation(User user, string plainPassword)
        {
            var bc = BlocksContext.GetContext();
            var tenantId = bc?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            user.Password = _identityAccessManagementService.HashPassword(plainPassword, tenant?.TenantSalt);
            user.PasswordSetTime = DateTime.Now;
            user.PasswordChangedAtUtc = DateTime.UtcNow;
            user.LastCredentialRotationAtUtc = DateTime.UtcNow;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.TokenVersion += 1;
            user.FailedLoginCount = 0;
            user.LastFailedLoginUtc = null;
            user.LockoutUntilUtc = null;
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

            // One answer for every email: unknown, blocked, active or pending, and also when the
            // lookup, the code store or the mail queue fails. The reason is logged only.
            try
            {
                var user = await _repository.GetUserByEmailAsync(recoveryRequest.Email);

                if (user == null || AccountCodes.IsBlocked(user))
                {
                    // Same reads as the mail path (the account, then the IAM configuration), so the
                    // answer does not come back noticeably sooner. No mail: an unknown address has
                    // nobody to write to, and only an administrator unblocks a blocked account.
                    await _repository.GetIamConfigurationAsync();
                    _logger.LogInformation(
                        user == null
                            ? "Forgot password requested for unknown email"
                            : "Forgot password ignored for blocked user");
                }
                else if (user.Active)
                {
                    await ProcessRecoverAccountAsync(
                        user,
                        AccountMailPurposes.RecoverAccount,
                        recoveryRequest.ClientId,
                        recoveryRequest.RedirectUri);
                }
                else
                {
                    _logger.LogInformation("Forgot-password routed to activation for inactive user {UserId}", user.ItemId);
                    await ProcessInactiveAccountRecoveryAsync(
                        user,
                        recoveryRequest.ClientId,
                        recoveryRequest.RedirectUri);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forgot password could not be processed");
            }

            return new BaseAccountResponse
            {
                IsSuccess = true
            };
        }

        private async Task ProcessInactiveAccountRecoveryAsync(User user, string? clientId = null, string? redirectUri = null)
        {
            await SendReActivationAsync(user, clientId, redirectUri);
        }

        public async Task<bool> ProcessRecoverAccountAsync(
            User user,
            string emailPurpose,
            string? clientId = null,
            string? redirectUri = null)
        {
            var config = await _repository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var bc = BlocksContext.GetContext();
            var path = $"{(config.IsOidcEnabled ? IdpConstants.OidcRecoverRoute + bc.TenantId : config.RecoverAccountPath)}?code={key}&lang={user.Language}";

            if (config.IsOidcEnabled)
            {
                path = await IamHelper.AppendOidcReturnContextAsync(path, clientId, redirectUri, _defaultOidcClientResolver);
            }

            if (!IamHelper.TryBuildUserActionUrl(
                    config, path, out var recoverAccountUrl, _httpContextAccessor,
                    logger: _logger, appConfiguration: _configuration))
            {
                _logger.LogWarning("Recover account URL could not be built for user {UserId}", user.ItemId);
                return false;
            }

            await AccountCodes.IssueAsync(_cacheClient, AccountCodes.Reset, key, user.ItemId, config.RecoverAccountUrlLifetimeInMinutes * 60);

            emailPurpose = string.IsNullOrWhiteSpace(emailPurpose) ? AccountMailPurposes.RecoverAccount : emailPurpose;
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
                To = new string[] { NormalizeEmail(user.Email) }
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
            var userId = await AccountCodes.ResolveAsync(_cacheClient, AccountCodes.Reset, resetPasswordRequest.Code);
            if (userId == null)
            {
                _logger.LogWarning("Password reset refused: the code is not a live reset code");
                return false;
            }

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {AccId}", userId);
                return false;
            }

            ApplyPasswordRotation(user, resetPasswordRequest.Password);

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                await AccountCodes.RetireAsync(_cacheClient, AccountCodes.Reset, resetPasswordRequest.Code, userId);
                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = userId,
                    Category = UserActivityCategory.Account,
                    Event = AccountEvents.ResetPassword,
                    Source = "iam-account"
                });

                // Always, whatever LogoutFromAllDevices says: a reset is how a user takes the account
                // back, so no session may survive it. Every Blocks reset form sends true anyway.
                await EndAllSessionsAsync(userId, "password_reset");
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

            var (result, locked) = await ChangePasswordCoreAsync(changePasswordRequest);

            if (locked)
            {
                return new BaseAccountResponse
                {
                    Errors = new Dictionary<string, string> { { AccountLockedErrorKey, AccountLockedMessage } }
                };
            }

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        /// <summary>Error key the API maps to HTTP 423, as login does for a locked account.</summary>
        public const string AccountLockedErrorKey = "AccountLocked";
        public const string AccountLockedMessage = "Account is temporarily locked due to failed login attempts";

        public async Task<bool> ProcessChangePasswordAsync(ChangePasswordRequest changePasswordRequest)
            => (await ChangePasswordCoreAsync(changePasswordRequest)).Result;

        private async Task<(bool Result, bool Locked)> ChangePasswordCoreAsync(ChangePasswordRequest changePasswordRequest)
        {
            var bc = BlocksContext.GetContext();

            var user = await _repository.GetUserByIdAsync(bc?.UserId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {Id}", bc?.UserId);
                return (false, false);
            }

            // Same lock and counter as login: while locked, a right and a wrong old password get
            // the same answer, and the old password is not even checked.
            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("Change password refused, account locked for user id: {Id}", user.ItemId);
                return (false, true);
            }

            var tenantId = bc?.TenantId;
            var tenant = !string.IsNullOrWhiteSpace(tenantId) ? _tenants.GetTenantByID(tenantId) : null;
            var passwordMatched = _identityAccessManagementService.VerifyPassword(changePasswordRequest.OldPassword, user.Password, tenant?.TenantSalt);
            if (!passwordMatched)
            {
                _logger.LogError("Password not matched");
                return (false, await RecordWrongOldPasswordAsync(user));
            }

            ApplyPasswordRotation(user, changePasswordRequest.NewPassword);

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                var config = await _repository.GetIamConfigurationAsync();
                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = bc?.UserId ?? string.Empty,
                    Category = UserActivityCategory.Account,
                    Event = AccountEvents.ChangePassword,
                    Source = "iam-account"
                });

                if (config.LogoutOnPasswordChange)
                {
                    await EndAllSessionsAsync(bc?.UserId ?? string.Empty, "password_change");
                }
            }
            return (result, false);
        }

        /// <summary>
        /// Counts a wrong old password on the login counter (same threshold and lock time).
        /// True when this attempt locked the account.
        /// </summary>
        private async Task<bool> RecordWrongOldPasswordAsync(User user)
        {
            if (_failedLoginTracker == null)
            {
                return false;
            }

            var updated = await _failedLoginTracker.RecordFailedAttemptAsync(user.ItemId);
            var lockoutUntilUtc = updated?.LockoutUntilUtc;
            if (!lockoutUntilUtc.HasValue || updated == null)
            {
                return false;
            }

            try
            {
                await SendAccountLockedNotificationAsync(updated, lockoutUntilUtc.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send account locked notification after failed password change for user: {UserId}", user.ItemId);
            }

            return true;
        }

        // Inline, so the sessions are dead before the response goes out; the logout-all queue is
        // only the backup for when that fails (or no terminator is wired).
        private async Task EndAllSessionsAsync(string userId, string reason)
        {
            if (_userSessionTerminator != null)
            {
                try
                {
                    await _userSessionTerminator.EndAllSessionsAsync(userId, reason);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ending sessions inline failed for user {UserId}; queueing logout-all", userId);
                }
            }

            await _identityAccessManagementService.SendToQueueAsync(IdpConstants.AuthenticationQueue, new LogoutAllEvent
            {
                UserId = userId
            });
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

        public async Task<bool> SendReActivationAsync(User user, string? clientId = null, string? redirectUri = null)
        {
            var config = await _repository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var bc = BlocksContext.GetContext();
            var path = $"{(config.IsOidcEnabled ? IdpConstants.OidcActivateRoute + bc.TenantId : config.AccountActivationPath)}?code={key}&lang={user.Language}";

            if (config.IsOidcEnabled)
            {
                path = await IamHelper.AppendOidcReturnContextAsync(path, clientId, redirectUri, _defaultOidcClientResolver);
            }

            if (!IamHelper.TryBuildUserActionUrl(
                    config, path, out var accountActivationUri, _httpContextAccessor,
                    logger: _logger, appConfiguration: _configuration))
            {
                _logger.LogWarning("Activation URL could not be built for user {UserId}", user.ItemId);
                return false;
            }

            await AccountCodes.IssueAsync(_cacheClient, AccountCodes.Activation, key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            var emailPurpose = string.IsNullOrWhiteSpace(user.MailPurpose) ? AccountMailPurposes.AccountActivation : user.MailPurpose;
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

        public async Task<BaseAccountResponse> ActivateAndLinkSocialIdentityAsync(ActivateAndLinkSocialIdentityRequest request)
        {
            if (request == null)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "request", "Request is required" }
                    }
                };
            }

            var result = await _userManagementMutationService.ActivateAndLinkSocialIdentityAsync(request);
            if (result.IsSuccess)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = true,
                    ItemId = result.ItemId,
                    Errors = null
                };
            }

            return new BaseAccountResponse
            {
                IsSuccess = false,
                ItemId = result.ItemId,
                Errors = (result.Errors as Dictionary<string, string>) ?? new Dictionary<string, string>
                {
                    { "activation_failed", "Account activation via social identity link failed." }
                }
            };
        }

        public async Task<ActivationCodeValidationResponse> ValidateAccountActivationCodeAsync(ValidateActivationCodeRequest validateActivationCodeRequest)
        {
            if (string.IsNullOrWhiteSpace(validateActivationCodeRequest.ActivationCode))
            {
                return new ActivationCodeValidationResponse
                {
                    Status = ActivationCodeStatus.Invalid,
                    Errors = new Dictionary<string, string>
                    {
                        { "ActivationCode", "ActivationCode_Required" }
                    }
                };
            }

            // The key map is the durable record and outlives the cache entry, so it still answers
            // for a code that has already been spent. Consulting it before the cache is what lets
            // a used link be told apart from one that never existed.
            var userId = await _repository.GetUserIdFromKeyMapByKeyAsync(validateActivationCodeRequest.ActivationCode);

            if (string.IsNullOrWhiteSpace(userId))
            {
                return new ActivationCodeValidationResponse
                {
                    Status = ActivationCodeStatus.Invalid,
                    Errors = new Dictionary<string, string>
                    {
                        { "ActivationCode", "Invalid_ActivationCode" }
                    }
                };
            }

            // A self-service signup already gave a name; returning it lets the activation
            // form prefill instead of asking twice. Holding the code already grants the
            // right to set this account's password, so the name is no new exposure.
            var user = await _repository.GetUserByIdAsync(userId);

            // A blocked account answers like a code that never existed: it cannot activate itself.
            if (user == null || AccountCodes.IsBlocked(user))
            {
                return new ActivationCodeValidationResponse
                {
                    Status = ActivationCodeStatus.Invalid,
                    Errors = new Dictionary<string, string>
                    {
                        { "ActivationCode", "Invalid_ActivationCode" }
                    }
                };
            }

            // The account is the authority on whether activation already happened. The key map's
            // own Activated flag is per-user rather than per-key, so it cannot say which of a
            // resent link's codes was the one spent.
            // Only a live, latest activation code returns account data. A spent, expired or
            // wrong-purpose code answers with its status alone: the key map never expires, so
            // anything more would hand out the account to whoever holds an old link.
            if (user.Active)
            {
                return new ActivationCodeValidationResponse
                {
                    Status = ActivationCodeStatus.AlreadyActivated,
                    IsSuccess = false
                };
            }

            var isCodeLive = await AccountCodes.ResolveAsync(_cacheClient, AccountCodes.Activation, validateActivationCodeRequest.ActivationCode) == userId;
            if (!isCodeLive)
            {
                return new ActivationCodeValidationResponse
                {
                    Status = ActivationCodeStatus.Expired,
                    IsSuccess = false
                };
            }

            return new ActivationCodeValidationResponse
            {
                UserId = userId,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                Status = ActivationCodeStatus.Valid,
                IsSuccess = true
            };
        }

        public async Task<SaveSignUpSettingResponse> SaveSignUpSettingAsync(SaveSignUpSettingRequest request)
        {
            // R6: signup settings are tenant-wide -- they hand roles to every future signup -- so
            // they belong to the system user.
            if (!_delegatedAdministration.AuthorizeSystemOnlyIfWired("signup-settings"))
            {
                return new SaveSignUpSettingResponse { IsSuccess = false, Errors = DelegatedAdministrationErrors.Create() };
            }

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
                    { "isSignUpEnable", false },
                    { "isEmailPasswordSignUpEnabled", false },
                    { "isSSoSignUpEnabled", false },
                    { "defaultRolesForNewUser", new List<string>() },
                    { "defaultPermissionsForNewUser", new List<string>() }
                };
            }

            return new Dictionary<string, object>
            {
                { "isSignUpEnable", tenantConfiguration.IsEmailPasswordSignUpEnabled || tenantConfiguration.IsSSoSignUpEnabled },
                { "isEmailPasswordSignUpEnabled", tenantConfiguration.IsEmailPasswordSignUpEnabled },
                { "isSSoSignUpEnabled", tenantConfiguration.IsSSoSignUpEnabled },
                { "defaultRolesForNewUser", tenantConfiguration.DefaultRolesForNewUserOnSignUp },
                { "defaultPermissionsForNewUser", tenantConfiguration.DefaultPermissionsForNewUserOnSignUp }
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
            ArgumentNullException.ThrowIfNull(user);

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
                    To = new[] { NormalizeEmail(user.Email) }
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
            ArgumentNullException.ThrowIfNull(user);

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
                    To = new[] { NormalizeEmail(user.Email) }
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

        private static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }
    }
}
