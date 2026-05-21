using Authentication.DomainService.Utilities;
using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Services
{
    public interface IOidcCallbackHandler
    {
        Task<OidcCallbackResult> HandleCallbackAsync(string code, string state);
    }

    public class OidcCallbackResult
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
        public string? RedirectUri { get; set; }
        public string? OriginalState { get; set; }
        
        // Session/User identification for post-callback setup
        public string? BlocksUserId { get; set; }
        public string? TenantId { get; set; }
    }

    public class OidcCallbackHandler : IOidcCallbackHandler
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
                var oidcSocialState = JsonDocument.Parse(oidcSocialStateJson).RootElement;
                var oidcState = oidcSocialState.GetProperty("oidcState").GetString();
                var provider = oidcSocialState.GetProperty("provider").GetString();

                if (string.IsNullOrWhiteSpace(oidcState))
                {
                    _logger.LogWarning("Invalid OIDC state in social callback");
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Invalid OIDC context" };
                }

                if (string.IsNullOrWhiteSpace(provider))
                {
                    _logger.LogWarning("Provider not found in OIDC social state");
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Provider not found in OIDC state" };
                }

                // 1. Get OIDC context (original client request)
                var contextKey = $"oidc_context:{oidcState}";
                var contextJson = await _cacheClient.GetStringValueAsync(contextKey);
                if (string.IsNullOrWhiteSpace(contextJson))
                {
                    _logger.LogWarning("OIDC context not found for state {OidcState}", oidcState);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "OIDC flow expired" };
                }

                var context = JsonDocument.Parse(contextJson).RootElement;
                var clientId = context.GetProperty("clientId").GetString();
                var originalState = context.GetProperty("state").GetString();
                var redirectUri = context.GetProperty("redirectUri").GetString();
                var providerClientId = context.GetProperty("providerClientId").GetString();
                var providerRedirectUri = context.GetProperty("providerRedirectUri").GetString();

                // 3. Reuse social folder callback handling for provider token exchange + user extraction
                var stateInfo = new StateInfo
                {
                    ClientId = providerClientId,
                    Provider = provider,
                    Code = code,
                    Audience = providerClientId,
                    RedirectUri = providerRedirectUri,
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
                var blocksUserId = await CreateOrUpdateUserFromExternalUserAsync(externalUserData, new List<string> { "user"}, new List<string>(), provider);
                if (string.IsNullOrWhiteSpace(blocksUserId))
                {
                    _logger.LogError("Failed to create/update user for provider {Provider}", provider);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Failed to create user account" };
                }

                // 6. Issue OIDC authorization code for the original client
                var authorizationCode = Guid.NewGuid().ToString("n");
                var authCodeKey = $"oidc_auth_code:{authorizationCode}";
                var authCodeValue = JsonSerializer.Serialize(new
                {
                    clientId,
                    userId = blocksUserId,  // Use Blocks user ID, not provider's user ID
                    provider = provider,
                    accessToken = socialCallbackResult.AccessToken ?? string.Empty,
                    idToken = socialCallbackResult.IdToken ?? string.Empty,
                    refreshToken = socialCallbackResult.RefreshToken ?? string.Empty,
                    expiresAt = DateTime.UtcNow.AddHours(1),
                    createdAt = DateTime.UtcNow
                });
                await _cacheClient.AddStringValueAsync(authCodeKey, authCodeValue, 300); // 5 minute TTL

                // 7. Clean up temporary states
                await _cacheClient.RemoveKeyAsync($"oidc_social_state:{state}");
                await _cacheClient.RemoveKeyAsync(contextKey);

                // 8. Return result with authorization code and redirect info
                return new OidcCallbackResult
                {
                    IsSuccess = true,
                    AuthorizationCode = authorizationCode,
                    RedirectUri = redirectUri,
                    OriginalState = originalState,
                    IsOidcFlow = true,
                    BlocksUserId = blocksUserId,
                    TenantId = context.GetProperty("tenantId").GetString() ?? "default"
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
        private async Task<string?> CreateOrUpdateUserFromExternalUserAsync(IExternalUserData externalUserData, List<string> roles, List<string> permissions, string provider, string orgId = "default")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(externalUserData.Email))
                    return null; // Cannot create user without email

                // Try to get existing user by email
                var existingUser = await _userRepository.GetUserByEmailAsync(externalUserData.Email);

                if (existingUser != null)
                {
                    return existingUser.ItemId;
                }

                // Create new user from social provider info
                var newUser = new Iam.DomainService.Entities.User
                {
                    Email = externalUserData.Email,
                    FirstName = externalUserData.FirstName ?? externalUserData.DisplayName,
                    LastName = externalUserData.LastName,
                    ProfileImageUrl = externalUserData.ProfileImageUrl,
                    PhoneNumber = externalUserData.PhoneNumber,
                    Platform = provider,
                    IsVerified = true,  // Trust social provider's email
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
                };

                // Save new user
                await _userRepository.CreateUserAsync(newUser);
                return newUser.ItemId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating/updating user from token for provider {Provider}", provider);
                return null;
            }
        }
    }
}
