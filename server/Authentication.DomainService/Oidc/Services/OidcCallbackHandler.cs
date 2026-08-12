using Iam.DomainService.Utilities;
using System.Security.Cryptography;
using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Dtos;
using Blocks.Genesis;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Services
{
    public interface IOidcCallbackHandler
    {
        Task<OidcCallbackResult> HandleCallbackAsync(string code, string state);
    }

    public sealed class OidcCallbackResult
    {
        public bool IsSuccess { get; set; }
        public string? AccessToken { get; set; }
        public string? IdToken { get; set; }
        public string? RefreshToken { get; set; }
        public Dictionary<string, object>? TokenPayload { get; set; }
        public string? ErrorMessage { get; set; }
        
        // OIDC flow specific fields
        public bool IsOidcFlow { get; set; } = false;
        public string? AuthorizationCode { get; set; }
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
        public string? OriginalState { get; set; }
        public string? Scope { get; set; }
        public string? Nonce { get; set; }
        public string? CodeChallenge { get; set; }
        public string? CodeChallengeMethod { get; set; }
        
        // Session/User identification for post-callback setup
        public string? BlocksUserId { get; set; }
        public string? TenantId { get; set; }
    }

    public sealed class OidcCallbackHandler : IOidcCallbackHandler
    {
        private const string DefaultOrganizationId = "default";

        /// <summary>
        /// Appended to a derived organization name when the name is already taken.
        /// Ambiguous characters (0/O, 1/I) are excluded so the suffix stays readable.
        /// </summary>
        private const string OrgSuffixAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int OrgSuffixLength = 5;
        private const int OrgNameMaxAttempts = 5;

        private readonly ILogger<OidcCallbackHandler> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IUserRepository _userRepository;
        private readonly ISocialLogInServiceProvider _socialLogInServiceProvider;
        private readonly IResourceMutationService _resourceMutationService;
        private readonly IResourceRepository _resourceRepository;

        public OidcCallbackHandler(
            ILogger<OidcCallbackHandler> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IUserRepository userRepository,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            IResourceMutationService resourceMutationService,
            IResourceRepository resourceRepository)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _resourceMutationService = resourceMutationService;
            _resourceRepository = resourceRepository;
            _userRepository = userRepository;
            _socialLogInServiceProvider = socialLogInServiceProvider;
        }

        public async Task<OidcCallbackResult> HandleCallbackAsync(string code, string state)
        {
            try
            {
                // Check if this is part of OIDC social flow (state is oidc_social_state)
                var oidcSocialStateJson = await _cacheClient.GetStringValueAsync($"oidc_social_state:{state}");
                
                if (string.IsNullOrWhiteSpace(oidcSocialStateJson))
                {
                    _logger.LogWarning("Invalid OIDC state: {State}", state);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Invalid or expired OIDC state" };
                }

                // OIDC SOCIAL FLOW - User authenticated via social provider within OIDC
                // Process and return authorization code for original OIDC client
                return await HandleOidcSocialCallbackAsync(code, state, oidcSocialStateJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OIDC callback");
                return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "An error occurred during token exchange" };
            }
        }


        /// <summary>
        /// Handle OIDC social login - issues authorization code for original OIDC client
        /// Flow: Frontend → OIDC Server → Social Provider → Backend → Issue Auth Code → Frontend
        /// </summary>
        private async Task<OidcCallbackResult> HandleOidcSocialCallbackAsync(string code, string state, string oidcSocialStateJson)
        {
            try
            {
                // Parse OIDC social state to get context
                var oidcSocialState = JsonSerializer.Deserialize<OidcSocialStateContext>(oidcSocialStateJson);
                if (oidcSocialState == null || string.IsNullOrWhiteSpace(oidcSocialState.OidcState))
                {
                    _logger.LogWarning("Invalid OIDC state in social callback");
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Invalid OIDC context" };
                }

                if (string.IsNullOrWhiteSpace(oidcSocialState.Provider))
                {
                    _logger.LogWarning("Provider not found in OIDC social state");
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Provider not found in OIDC state" };
                }

                var oidcState = oidcSocialState.OidcState;
                var provider = oidcSocialState.Provider;

                // 1. Get OIDC context (original client request)
                var contextKey = $"oidc_context:{oidcState}";
                var contextJson = await _cacheClient.GetStringValueAsync(contextKey);
                if (string.IsNullOrWhiteSpace(contextJson))
                {
                    _logger.LogWarning("OIDC context not found for state {OidcState}", oidcState);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "OIDC flow expired" };
                }

                var context = JsonSerializer.Deserialize<OidcContext>(contextJson);
                if (context == null)
                {
                    _logger.LogWarning("Failed to deserialize OIDC context for state {OidcState}", oidcState);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "OIDC flow expired" };
                }

                // 3. Reuse social folder callback handling for provider token exchange + user extraction
                var stateInfo = new StateInfo
                {
                    ClientId = context.ProviderClientId,
                    Provider = provider,
                    Code = code,
                    Audience = context.ProviderClientId,
                    RedirectUri = context.ProviderRedirectUri,
                    FlowType = SocialFlowType.Oidc
                };

                var socialCallbackResult = await _socialLogInServiceProvider.HandleSocialLoginCallback(stateInfo);
                var externalUserData = socialCallbackResult.ExternalUserData;

                if (externalUserData == null || string.IsNullOrWhiteSpace(externalUserData.Email))
                {
                    _logger.LogError("Social provider callback did not return user email for provider {Provider}", provider);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Provider did not return a valid user email" };
                }

                // 5. Create or update Blocks user based on provider's user info.
                // Roles come from tenant signup config so SSO and email signups land on
                // the same defaults; the org is created only when the tenant allows it.
                var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
                var signupRoles = tenantConfig?.DefaultRolesForNewUserOnSignUp ?? new List<string>();
                var signupPermissions = tenantConfig?.DefaultPermissionsForNewUserOnSignUp ?? new List<string>();

                var ssoUser = await CreateOrUpdateUserFromExternalUserAsync(externalUserData, signupRoles, signupPermissions, provider, tenantConfig);

                if (string.IsNullOrWhiteSpace(ssoUser.userId))
                {
                    _logger.LogError("Failed to create/update user for provider {Provider}", provider);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Failed to create user account" };
                }

                if(!ssoUser.isactive)
                {
                    _logger.LogError($"user with id {ssoUser.userId} is not active or verified", provider);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "user is not active" };
                }

                // 6. Clean up temporary states. The actual authorization code must be created
                // through AuthorizationFlowService.AuthorizeAsync so it persists as AuthorizationCodeModel.
                await _cacheClient.RemoveKeyAsync($"oidc_social_state:{state}");
                await _cacheClient.RemoveKeyAsync(contextKey);

                // 7. Return the recovered OIDC request context so the controller can continue
                // through the same AuthorizeAsync path used by password-based OIDC login.
                return new OidcCallbackResult
                {
                    IsSuccess = true,
                    ClientId = context.ClientId,
                    RedirectUri = context.RedirectUri,
                    OriginalState = context.State,
                    IsOidcFlow = true,
                    BlocksUserId = ssoUser.userId,
                    TenantId = string.IsNullOrWhiteSpace(context.TenantId) ? "default" : context.TenantId,
                    Scope = context.Scope,
                    Nonce = context.Nonce,
                    CodeChallenge = context.CodeChallenge,
                    CodeChallengeMethod = context.CodeChallengeMethod
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OIDC social callback for provider");
                return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "An error occurred during OIDC processing" };
            }
        }

        /// <summary>
        /// Create or update Blocks user from social provider's normalized user info
        /// Returns Blocks user ID
        /// </summary>
        private async Task<(string? userId, bool isactive)> CreateOrUpdateUserFromExternalUserAsync(IExternalUserData externalUserData, List<string> roles, List<string> permissions, string provider, TenantConfiguration? tenantConfig = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(externalUserData.Email))
                    return (null, false); // Cannot create user without email

                var normalizedEmail = NormalizeEmail(externalUserData.Email);

                // Try to get existing user by email
                var existingUser = await _userRepository.GetUserByEmailAsync(normalizedEmail);

                if (existingUser != null)
                {
                    // Existing members keep the organizations they already belong to —
                    // this is a login, not a signup.
                    return (existingUser.ItemId, existingUser.Active);
                }

                // Id is needed up front so the organization records this user as creator.
                var newUserId = Guid.NewGuid().ToString();
                var orgId = await CreateSignupOrganizationAsync(externalUserData, tenantConfig, newUserId);

                // Create new user from social provider info
                var newUser = new Iam.DomainService.Entities.User
                {
                    ItemId = newUserId,
                    Email = normalizedEmail,
                    UserName = normalizedEmail,
                    FirstName = externalUserData.FirstName ?? externalUserData.DisplayName,
                    LastName = externalUserData.LastName,
                    ProfileImageUrl = externalUserData.ProfileImageUrl,
                    PhoneNumber = externalUserData.PhoneNumber,
                    Platform = provider,
                    Active = true,
                    IsVerified = true,
                    Status = Iam.DomainService.Entities.UserLifecycleStatus.Active,
                    StatusReason = "social_signup",
                    ProvisioningSource = Iam.DomainService.Entities.UserProvisioningSource.Social,

                    Roles = new Dictionary<string, List<string>>
                    {
                        { orgId, roles }
                    },
                    Permissions = new Dictionary<string, List<string>>
                    {
                        { orgId, permissions }
                    },

                    OrganizationIds = new List<string> { orgId },
                    Attributes = provider == "microsoft" ? new Dictionary<string, object>
                    {
                        { "Department", externalUserData.Department },
                        { "EmployeeId", externalUserData.EmployeeId },
                        { "ExternalProviderUserId", externalUserData.ExternalProviderUserId }
                    } : new Dictionary<string, object>(),

                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow
                };

                // Save new user
                await _userRepository.CreateUserAsync(newUser);
                return (newUser.ItemId, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating/updating user from token for provider {Provider}", provider);
                return (null, false);
            }
        }

        /// <summary>
        /// Creates an organization for a brand-new SSO user, named after them. Returns the
        /// default organization id when the tenant does not allow org creation from signup,
        /// or when creation fails — a login must not break because an org could not be made.
        /// </summary>
        private async Task<string> CreateSignupOrganizationAsync(
            IExternalUserData externalUserData,
            TenantConfiguration? tenantConfig,
            string creatorUserId)
        {
            // Mirrors the gates inside CreateOrganizationAsync. Checked here as well so a
            // null tenant config short-circuits before it is dereferenced downstream.
            if (tenantConfig == null
                || !tenantConfig.IsMultiOrgEnabled
                || !tenantConfig.AllowOrgCreationFromSignup)
            {
                return DefaultOrganizationId;
            }

            try
            {
                var organizationName = await ResolveAvailableOrganizationNameAsync(externalUserData);
                if (string.IsNullOrWhiteSpace(organizationName))
                {
                    return DefaultOrganizationId;
                }

                var result = await _resourceMutationService.CreateOrganizationAsync(
                    new CreateOrganizationRequest
                    {
                        Name = organizationName,
                        CreatedFrom = CreatedFrom.ConstructSignup
                    },
                    creatorUserId);

                if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ItemId))
                {
                    _logger.LogWarning(
                        "Organization creation skipped for SSO signup {Email}; falling back to default org",
                        NormalizeEmail(externalUserData.Email));
                    return DefaultOrganizationId;
                }

                return result.ItemId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating organization during SSO signup; falling back to default org");
                return DefaultOrganizationId;
            }
        }

        /// <summary>
        /// "{FirstName} {LastName} Organization", with a random suffix appended when that
        /// name is taken. Organization names are unique case-insensitively, so a plain
        /// duplicate would otherwise fail the signup outright.
        /// </summary>
        private async Task<string> ResolveAvailableOrganizationNameAsync(IExternalUserData externalUserData)
        {
            var baseName = BuildOrganizationBaseName(externalUserData);

            if (await _resourceRepository.GetOrganizationByNameAsync(baseName) == null)
            {
                return baseName;
            }

            for (var attempt = 0; attempt < OrgNameMaxAttempts; attempt++)
            {
                var candidate = $"{baseName} {GenerateOrgSuffix()}";
                if (await _resourceRepository.GetOrganizationByNameAsync(candidate) == null)
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string BuildOrganizationBaseName(IExternalUserData externalUserData)
        {
            // FirstName already falls back to DisplayName at the call site above; when the
            // provider sends neither, a random token stands in so the name is never just
            // the bare " Organization" suffix.
            var parts = new[] { externalUserData.FirstName, externalUserData.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim());

            var personName = string.Join(" ", parts);

            if (string.IsNullOrWhiteSpace(personName))
            {
                personName = GenerateOrgSuffix();
            }

            const int maxPersonNameLength = 60;
            if (personName.Length > maxPersonNameLength)
            {
                personName = personName[..maxPersonNameLength].TrimEnd();
            }

            return $"{personName} Organization";
        }

        private static string GenerateOrgSuffix()
        {
            return RandomNumberGenerator.GetString(OrgSuffixAlphabet, OrgSuffixLength);
        }

        private static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }
    }
}
