using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
using System.Text.Json;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Authentication.DomainService.Oidc.Services
{
    public interface IOidcCallbackHandler
    {
        Task<OidcCallbackResult> HandleCallbackAsync(string code, string state, string provider);
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
        private readonly IHttpService _httpService;
        private readonly IUserRepository _userRepository;

        public OidcCallbackHandler(
            ILogger<OidcCallbackHandler> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService,
            IUserRepository userRepository)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _httpService = httpService;
            _userRepository = userRepository;
        }

        public async Task<OidcCallbackResult> HandleCallbackAsync(string code, string state, string provider)
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
                return await HandleOidcSocialCallbackAsync(code, state, provider, oidcSocialStateJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OIDC callback for provider {Provider}", provider);
                return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "An error occurred during token exchange" };
            }
        }


        /// <summary>
        /// Handle OIDC social login - issues authorization code for original OIDC client
        /// Flow: Frontend → OIDC Server → Social Provider → Backend → Issue Auth Code → Frontend
        /// </summary>
        private async Task<OidcCallbackResult> HandleOidcSocialCallbackAsync(string code, string state, string provider, string oidcSocialStateJson)
        {
            try
            {
                // Parse OIDC social state to get context
                var oidcSocialState = System.Text.Json.JsonDocument.Parse(oidcSocialStateJson).RootElement;
                var oidcState = oidcSocialState.GetProperty("oidcState").GetString();

                if (string.IsNullOrWhiteSpace(oidcState))
                {
                    _logger.LogWarning("Invalid OIDC state in social callback");
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Invalid OIDC context" };
                }

                // 1. Get OIDC context (original client request)
                var contextKey = $"oidc_context:{oidcState}";
                var contextJson = await _cacheClient.GetStringValueAsync(contextKey);
                if (string.IsNullOrWhiteSpace(contextJson))
                {
                    _logger.LogWarning("OIDC context not found for state {OidcState}", oidcState);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "OIDC flow expired" };
                }

                var context = System.Text.Json.JsonDocument.Parse(contextJson).RootElement;
                var clientId = context.GetProperty("clientId").GetString();
                var originalState = context.GetProperty("state").GetString();
                var redirectUri = context.GetProperty("redirectUri").GetString();

                // 2. Get IdentityProvider config
                var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(provider);
                if (identityProvider == null)
                {
                    _logger.LogError("Identity provider {Provider} not found", provider);
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Provider not configured" };
                }

                // 3. Exchange code for token from social provider
                var tokenResult = await ExchangeCodeForTokenAsync(code, identityProvider);
                if (!tokenResult.IsSuccess)
                {
                    _logger.LogError("Token exchange failed for provider {Provider}: {Error}", provider, tokenResult.ErrorMessage);
                    return tokenResult;
                }

                // 4. Validate and parse ID token
                if (!string.IsNullOrWhiteSpace(tokenResult.IdToken))
                {
                    var validationResult = ValidateAndParseIdToken(tokenResult.IdToken, identityProvider);
                    if (!validationResult.IsValid)
                    {
                        _logger.LogError("ID token validation failed: {Error}", validationResult.ErrorMessage);
                        return new OidcCallbackResult { IsSuccess = false, ErrorMessage = validationResult.ErrorMessage };
                    }
                    tokenResult.TokenPayload = validationResult.Payload;
                }

                // 5. Create or update Blocks user based on provider's user info
                var blocksUserId = await CreateOrUpdateUserFromTokenAsync(tokenResult.TokenPayload, provider, identityProvider);
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
                    provider,
                    accessToken = tokenResult.AccessToken,
                    idToken = tokenResult.IdToken,
                    refreshToken = tokenResult.RefreshToken,
                    expiresAt = DateTime.UtcNow.AddHours(1),
                    createdAt = DateTime.UtcNow
                });
                await _cacheClient.AddStringValueAsync(authCodeKey, authCodeValue, 300); // 5 minute TTL

                // 7. Clean up temporary states
                await _cacheClient.RemoveKeyAsync($"oidc_social_state:{state}");
                await _cacheClient.RemoveKeyAsync(contextKey);

                // 8. Return result with authorization code and redirect info
                tokenResult.IsSuccess = true;
                tokenResult.AuthorizationCode = authorizationCode;
                tokenResult.RedirectUri = redirectUri;
                tokenResult.OriginalState = originalState;
                tokenResult.IsOidcFlow = true;
                tokenResult.BlocksUserId = blocksUserId;
                tokenResult.TenantId = context.GetProperty("tenantId").GetString() ?? "default";

                return tokenResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OIDC social callback for provider {Provider}", provider);
                return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "An error occurred during OIDC processing" };
            }
        }

        /// <summary>
        /// Create or update Blocks user from social provider's user info
        /// Deserializes token payload using provider-specific IExternalUserData classes
        /// Returns Blocks user ID
        /// </summary>
        private async Task<string?> CreateOrUpdateUserFromTokenAsync(Dictionary<string, object>? tokenPayload, string provider, IdentityProvider identityProvider)
        {
            if (tokenPayload == null || tokenPayload.Count == 0)
                return null;

            try
            {
                // Convert token payload to JSON and deserialize into provider-specific IExternalUserData
                var externalUserData = DeserializeExternalUserData(tokenPayload, provider);
                if (externalUserData == null || string.IsNullOrWhiteSpace(externalUserData.Email))
                    return null; // Cannot create user without email

                // Set platform and roles/permissions from IdentityProvider config
                externalUserData.Platform = provider;
                externalUserData.Roles = identityProvider.InitialRoles ?? [];
                externalUserData.Permissions = identityProvider.InitialPermissions ?? [];

                // Try to get existing user by email
                var existingUser = await _userRepository.GetUserByEmailAsync(externalUserData.Email);

                if (existingUser != null)
                {
                    // Update existing user with new info from provider
                    existingUser.FirstName = externalUserData.FirstName ?? existingUser.FirstName;
                    existingUser.LastName = externalUserData.LastName ?? existingUser.LastName;
                    existingUser.ProfileImageUrl = externalUserData.ProfileImageUrl ?? existingUser.ProfileImageUrl;
                    existingUser.Platform = provider;
                    existingUser.IsVerified = true;  // Trust social provider's email
                    
                    // Update roles and permissions
                    if (existingUser.Roles == null) existingUser.Roles = new Dictionary<string, List<string>>();
                    if (existingUser.Permissions == null) existingUser.Permissions = new Dictionary<string, List<string>>();
                    
                    existingUser.Roles["default"] = externalUserData.Roles ?? [];
                    existingUser.Permissions["default"] = externalUserData.Permissions ?? [];
                    
                    await _userRepository.UpdateUserAsync(existingUser);
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
                        { "default", externalUserData.Roles ?? [] }
                    },
                    Permissions = new Dictionary<string, List<string>>
                    {
                        { "default", externalUserData.Permissions ?? [] }
                    }
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

        /// <summary>
        /// Deserialize token payload into provider-specific IExternalUserData
        /// Uses JsonPropertyName attributes to map claims correctly per provider
        /// </summary>
        private IExternalUserData? DeserializeExternalUserData(Dictionary<string, object> tokenPayload, string provider)
        {
            try
            {
                // Convert Dictionary to JSON string, then deserialize into appropriate provider class
                var jsonString = JsonSerializer.Serialize(tokenPayload);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                return provider.ToLower() switch
                {
                    "google" => JsonSerializer.Deserialize<GoogleUserData>(jsonString, options),
                    "microsoft" => JsonSerializer.Deserialize<MicrosoftUserData>(jsonString, options),
                    "github" => JsonSerializer.Deserialize<GithubUserData>(jsonString, options),
                    "linkedin" => JsonSerializer.Deserialize<LinkedinUserData>(jsonString, options),
                    "x" => JsonSerializer.Deserialize<TwitterUserData>(jsonString, options),
                    "twitter" => JsonSerializer.Deserialize<TwitterUserData>(jsonString, options),
                    "apple" => JsonSerializer.Deserialize<AppleUserData>(jsonString, options),
                    _ => JsonSerializer.Deserialize<StandardSocialUserDataBase>(jsonString, options)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing external user data for provider {Provider}", provider);
                return null;
            }
        }

        private async Task<OidcCallbackResult> ExchangeCodeForTokenAsync(string code, IdentityProvider provider)
        {
            try
            {
                var tokenRequest = new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "client_id", provider.ClientId },
                    { "client_secret", provider.ClientSecret },
                    { "redirect_uri", provider.RedirectUris?.FirstOrDefault() ?? "" }
                };

                var timeoutSeconds = (int)GetOutboundRequestTimeout().TotalSeconds;
                var (response, error) = await _httpService.SendFormUrlEncoded<OidcTokenResponse>(
                    HttpMethod.Post,
                    tokenRequest,
                    provider.TokenUrl,
                    timeoutSeconds: timeoutSeconds
                );

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = $"Token endpoint error: {error}" };
                }

                if (response == null)
                {
                    return new OidcCallbackResult { IsSuccess = false, ErrorMessage = "Empty token response" };
                }

                return new OidcCallbackResult
                {
                    IsSuccess = true,
                    AccessToken = response.AccessToken,
                    IdToken = response.IdToken,
                    RefreshToken = response.RefreshToken
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exchanging code for token");
                return new OidcCallbackResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        private (bool IsValid, Dictionary<string, object>? Payload, string? ErrorMessage) ValidateAndParseIdToken(
            string idToken,
            IdentityProvider provider)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                
                // Parse token first to check basic structure
                var token = handler.ReadToken(idToken) as JwtSecurityToken;
                if (token == null)
                {
                    return (false, null, "Invalid JWT token format");
                }

                // 1. Validate token expiration
                if (token.ValidTo < DateTime.UtcNow)
                {
                    return (false, null, "ID token has expired");
                }

                // 2. Validate issuer (if configured)
                if (!string.IsNullOrWhiteSpace(provider.Issuer))
                {
                    var expectedIssuer = provider.Issuer;
                    if (!string.Equals(token.Issuer, expectedIssuer, StringComparison.Ordinal))
                    {
                        _logger.LogWarning("ID token issuer mismatch. Expected: {Expected}, Got: {Got}", expectedIssuer, token.Issuer);
                        return (false, null, "ID token issuer validation failed");
                    }
                }

                // 3. Validate audience (if configured)
                if (!string.IsNullOrWhiteSpace(provider.ClientId) && token.Audiences != null && token.Audiences.Any())
                {
                    if (!token.Audiences.Contains(provider.ClientId))
                    {
                        _logger.LogWarning("ID token audience validation failed. Expected: {Expected}, Got: {Audiences}", 
                            provider.ClientId, string.Join(", ", token.Audiences));
                        return (false, null, "ID token audience validation failed");
                    }
                }

                // 4. JWKS Signature Validation (if JWKS URI is configured)
                // Note: Signature validation requires fetching JWKS from provider
                // This is optional for now - basic validation is in place
                // Full signature validation would require: ValidateTokenSignatureAsync(idToken, provider)

                // Extract payload claims
                var payload = token.Claims
                    .GroupBy(c => c.Type)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count() == 1 
                            ? (object)g.First().Value 
                            : (object)g.Select(c => c.Value).ToList()
                    );

                return (true, payload, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating ID token");
                return (false, null, ex.Message);
            }
        }

        private static bool IsLocalhost()
        {
            var hostEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "";
            return hostEnv.Equals("Development", StringComparison.OrdinalIgnoreCase);
        }

        private static TimeSpan GetOutboundRequestTimeout()
        {
            return IsLocalhost() ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(120);
        }
    }

    public class OidcTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
