using Iam.DomainService.Utilities;
using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Dtos;
using Blocks.Genesis;
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
        private readonly ILogger<OidcCallbackHandler> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IUserRepository _userRepository;
        private readonly ISocialLogInServiceProvider _socialLogInServiceProvider;

        public OidcCallbackHandler(
            ILogger<OidcCallbackHandler> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IUserRepository userRepository,
            ISocialLogInServiceProvider socialLogInServiceProvider)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
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

                // 5. Create or update Blocks user based on provider's user info
                var ssoUser = await CreateOrUpdateUserFromExternalUserAsync(externalUserData, new List<string> { "user"}, new List<string>(), provider);

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
        private async Task<(string? userId, bool isactive)> CreateOrUpdateUserFromExternalUserAsync(IExternalUserData externalUserData, List<string> roles, List<string> permissions, string provider, string orgId = "default")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(externalUserData.Email))
                    return (null, false); // Cannot create user without email

                // Try to get existing user by email
                var existingUser = await _userRepository.GetUserByEmailAsync(externalUserData.Email);

                if (existingUser != null)
                {
                    return (existingUser.ItemId, existingUser.Active);
                }

                // Create new user from social provider info
                var newUser = new Iam.DomainService.Entities.User
                {
                    ItemId = Guid.NewGuid().ToString(),
                    Email = externalUserData.Email,
                    FirstName = externalUserData.FirstName ?? externalUserData.DisplayName,
                    LastName = externalUserData.LastName,
                    ProfileImageUrl = externalUserData.ProfileImageUrl,
                    PhoneNumber = externalUserData.PhoneNumber,
                    Platform = provider,
                    Active = true,

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
    }
}
