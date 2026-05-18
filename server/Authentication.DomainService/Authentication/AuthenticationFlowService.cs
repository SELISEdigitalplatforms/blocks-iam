using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    public class AuthenticationFlowService : IAuthenticationFlowService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly PasswordAuthenticationService _passwordAuthenticationService;
        private readonly SocialAuthorizationService _socialAuthorizationService;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IAuthenticationService _authenticationService;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<AuthenticationFlowService> _logger;

        public AuthenticationFlowService(
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            PasswordAuthenticationService passwordAuthenticationService,
            SocialAuthorizationService socialAuthorizationService,
            RefreshTokenAuthenticationService refreshTokenAuthenticationService,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationService authenticationService,
            ICacheClient cacheClient,
            ILogger<AuthenticationFlowService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _passwordAuthenticationService = passwordAuthenticationService;
            _socialAuthorizationService = socialAuthorizationService;
            _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _authenticationService = authenticationService;
            
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public async Task<AuthenticationFlowResult> ExecuteEmbeddedLoginAsync(EmbeddedLoginRequest request, HttpRequest httpRequest)
        {
            var clientId = ResolveClientId(httpRequest, request.ClientId);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "invalid_client",
                    ErrorDescription = "client_id is required"
                };
            }

            if (!await HasOidcClientConfigurationAsync(clientId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "invalid_client",
                    ErrorDescription = "Client configuration not found"
                };
            }

            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "auth_config_missing"
                };
            }

            var user = await _authenticationRepository.GetUserByUsernameAsync(request.Username);
            var resolvedOrganizationId = ResolveOrgIdFromUser(user);

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.Password,
                ClientId = clientId,
                Username = request.Username,
                Password = request.Password,
                OrganizationId = resolvedOrganizationId,
                Request = httpRequest
            };

            return new AuthenticationFlowResult
            {
                TokenResponse = await _passwordAuthenticationService.AuthenticateAsync(tokenRequest, configuration)
            };
        }

        public async Task<AuthenticationFlowResult> ExecuteSocialLoginAsync(SocialLoginRequest request, HttpRequest httpRequest)
        {
            var clientId = ResolveClientId(httpRequest, request.ClientId);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "invalid_client",
                    ErrorDescription = "client_id is required"
                };
            }

            if (!await HasOidcClientConfigurationAsync(clientId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "invalid_client",
                    ErrorDescription = "Client configuration not found"
                };
            }

            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "auth_config_missing"
                };
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.Social,
                ClientId = clientId,
                Code = request.Code,
                State = request.State,
                Request = httpRequest
            };

            return new AuthenticationFlowResult
            {
                TokenResponse = await _socialAuthorizationService.AuthenticateAsync(tokenRequest, configuration)
            };
        }

        private static string ResolveOrgIdFromUser(User? user)
        {
            if (user == null)
            {
                return "default";
            }

            if (HasOrganizationAccess(user, user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId!;
            }

            if (HasOrganizationAccess(user, "default"))
            {
                return "default";
            }

            return user.OrganizationIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? user.Roles.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? user.Permissions.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? "default";
        }

        private static bool HasOrganizationAccess(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            return user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId);
        }

        public async Task<AuthenticationFlowResult> ExecuteSwitchOrganizationAsync(SwitchOrganizationRequest request, ClaimsPrincipal principal, HttpRequest httpRequest)
        {
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "auth_config_missing"
                };
            }

            if (string.IsNullOrWhiteSpace(request.OrganizationId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "invalid_request",
                    ErrorDescription = "organization_id is required"
                };
            }

            var tenantId = principal.FindFirstValue(BlocksContext.TENANT_ID_CLAIM)
                ?? BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "tenant_not_resolved"
                };
            }

            var userId = principal.FindFirstValue(BlocksContext.USER_ID_CLAIM)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "invalid_user"
                };
            }

            var user = await _authenticationRepository.GetUserByIdAsync(userId);
            var hasOrganizationAccess = user != null
                && (
                    user.OrganizationIds.Contains(request.OrganizationId)
                    || user.Roles.ContainsKey(request.OrganizationId)
                    || user.Permissions.ContainsKey(request.OrganizationId)
                );

            if (!hasOrganizationAccess)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "organization_not_available"
                };
            }

            var refreshToken = _authenticationService.CookieToken(httpRequest);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "session_expired"
                };
            }

            var refreshCacheRaw = await _cacheClient.GetStringValueAsync(refreshToken);
            var refreshCache = string.IsNullOrWhiteSpace(refreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(refreshCacheRaw);

            if (refreshCache == null || refreshCache.ExpiresUtc <= DateTime.UtcNow)
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "session_expired"
                };
            }

            if (!string.Equals(refreshCache.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(refreshCache.UserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "session_expired"
                };
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.SwitchOrganization,
                ClientId = refreshCache.ClientId,
                OrganizationId = request.OrganizationId,
                RefreshToken = refreshToken,
                Request = httpRequest
            };

            if (string.IsNullOrWhiteSpace(tokenRequest.ClientId) || !await HasOidcClientConfigurationAsync(tokenRequest.ClientId))
            {
                return new AuthenticationFlowResult
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "invalid_client",
                    ErrorDescription = "Client configuration not found"
                };
            }

            return new AuthenticationFlowResult
            {
                TokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, configuration, user!)
            };
        }

        public async Task<IActionResult> ExecuteRefreshAsync(RefreshRequest request, ClaimsPrincipal principal, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return new BadRequestObjectResult(new { error = "auth_config_missing" });
            }

            var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
                ? _authenticationService.CookieToken(httpRequest)
                : request.RefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is required" });
            }

            var cachedRefreshToken = await _cacheClient.GetStringValueAsync(refreshToken);
            if (string.IsNullOrWhiteSpace(cachedRefreshToken))
            {
                await HandlePotentialRefreshTokenReuseAsync(refreshToken);
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
            }

            var tokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(cachedRefreshToken);
            if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
            {
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
            }

            if (string.IsNullOrWhiteSpace(tokenCache.ClientId) || !await HasOidcClientConfigurationAsync(tokenCache.ClientId))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" });
            }

            // Defense-in-depth: Validate sent client_id matches the cached/bound client_id
            if (!string.IsNullOrWhiteSpace(request.ClientId) && 
                !string.Equals(request.ClientId, tokenCache.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_client", error_description = "Client mismatch: sent client_id does not match token binding" });
            }

            var currentTenantId = BlocksContext.GetContext()?.TenantId;

            if (!string.Equals(tokenCache.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token tenant mismatch" });
            }

            var user = await _authenticationRepository.GetUserByIdAsync(tokenCache.UserId);
            if (user == null)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.RefreshToken,
                OrganizationId = string.IsNullOrWhiteSpace(tokenCache.OrganizationId) ? "default" : tokenCache.OrganizationId,
                ClientId = tokenCache.ClientId,
                RefreshToken = refreshToken,
                Request = httpRequest
            };

            var result = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);

            return await BuildTokenResponseAsync(httpResponse, result);
        }

        private async Task HandlePotentialRefreshTokenReuseAsync(string refreshToken)
        {
            await _authenticationRepository.RevokeIdentitySessionsByRefreshTokensAsync(new List<string> { refreshToken });

            await _cacheClient.RemoveKeyAsync(refreshToken);
            _logger.LogWarning("Potential refresh token reuse detected for token {RefreshToken}. Existing session revoked.", refreshToken);
        }

        private static string? ResolveClientId(HttpRequest request, string? modelClientId = null)
        {
            if (!string.IsNullOrWhiteSpace(modelClientId))
            {
                return modelClientId;
            }

            if (request == null)
            {
                return null;
            }

            var queryClientId = request.Query["client_id"].ToString();
            if (!string.IsNullOrWhiteSpace(queryClientId))
            {
                return queryClientId;
            }

            var formClientId = request.HasFormContentType ? request.Form["client_id"].ToString() : string.Empty;
            if (!string.IsNullOrWhiteSpace(formClientId))
            {
                return formClientId;
            }

            var headerClientId = request.Headers["X-Client-Id"].ToString();
            return string.IsNullOrWhiteSpace(headerClientId) ? null : headerClientId;
        }

        private async Task<bool> HasOidcClientConfigurationAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            var oidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            return oidcClient != null;
        }

        private async Task<bool> IsTenantSharedWithUserAsync(string userId, string targetTenantId)
        {
            try
            {
                var collection = _authenticationRepository.GetCollectionByName<BsonDocument>("ProjectPeoples");
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("UserId", userId),
                    Builders<BsonDocument>.Filter.Eq("TenantId", targetTenantId)
                );

                return await collection.Find(filter).Limit(1).AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate ProjectPeople share for user {UserId} and tenant {TenantId}", userId, targetTenantId);
                return false;
            }
        }

        private async Task<IActionResult> BuildTokenResponseAsync(HttpResponse httpResponse, TokenResponse response)
        {
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                var statusCode = response.StatusCode > 0 ? response.StatusCode : StatusCodes.Status400BadRequest;
                return new ObjectResult(new
                {
                    error = response.Error,
                    error_description = response.ErrorDescription,
                    redirect_url = response.SsoUserRedirectUrl
                })
                {
                    StatusCode = statusCode
                };
            }

            var clientId = TryGetClientIdFromAccessToken(response.AccessToken);
            var useTokensCookie = await ResolveUseTokensCookieAsync(clientId);

            if (useTokensCookie)
            {
                var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
                var tenant = _tenants.GetTenantByID(tenantId);
                var cookiesSet = AppendCookies(httpResponse, response, tenant, httpResponse.HttpContext?.Request);
                if (cookiesSet)
                {
                    return new OkObjectResult(new
                    {
                        token_type = response.TokenType,
                        expires_in = response.ExpiresIn,
                        scope = response.Scope,
                        client_id = clientId,
                        cookie_set = true
                    });
                }
            }

            return new OkObjectResult(new
            {
                access_token = response.AccessToken,
                refresh_token = response.RefreshToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                scope = response.Scope,
                id_token = response.IdToken,
                client_id = clientId,
                cookie_set = false
            });
        }

        private async Task<bool> ResolveUseTokensCookieAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return true;
            }

            var registration = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            return registration?.UseTokensCookie ?? true;
        }

        private static string? TryGetClientIdFromAccessToken(string? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
                return jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value
                    ?? jwt.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
            }
            catch
            {
                return null;
            }
        }

        private bool AppendCookies(HttpResponse httpResponse, TokenResponse response, Tenant? tenant, HttpRequest? httpRequest)
        {
            // Validate response has no error indicator
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return false; // Cannot set cookies for error responses
            }

            // Validate access token is present
            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                return false; // Cannot set cookies without valid access token
            }

            var (tokenDomain, _, isResolved) = DomainResolver.ResolveDomain(tenant, httpRequest);
            if (!isResolved || string.IsNullOrWhiteSpace(tokenDomain))
            {
                return false;
            }
            var accessCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            httpResponse.Cookies.Append(response.CookieDomain , response.AccessToken, accessCookieOptions);

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{response.CookieDomain}", response.RefreshToken, refreshCookieOptions);
            }

            return true;
        }

        private static CookieOptions CreateCookieOptions(string? domain, DateTime expiresUtc)
        {
            var isLocal = DomainResolver.IsLocalhost();
            var cookieDomain = isLocal ? null : (string.IsNullOrWhiteSpace(domain) ? null : domain);
            return new CookieOptions
            {
                Domain = cookieDomain,
                HttpOnly = true,
                Secure = !isLocal,
                SameSite = isLocal ? SameSiteMode.None : SameSiteMode.Strict,
                Path = "/",
                Expires = expiresUtc == default ? DateTime.UtcNow : expiresUtc
            };
        }

        public async Task<GetContextForProjectResult> GetContextForProjectAsync(string projectId)
        {
            var bc = BlocksContext.GetContext();
            var isRootTenant = _tenants.GetTenantByID(bc.TenantId)?.IsRootTenant ?? false;

            if (!isRootTenant)
            {
                return new GetContextForProjectResult
                {
                    IsSuccess = false,
                    Error = "access_denied",
                    ContextId = null
                };
            }

            if (string.IsNullOrWhiteSpace(projectId)) 
            {
                return new GetContextForProjectResult
                {
                    IsSuccess = false,
                    Error = "invalid_project_id",
                    ContextId = null
                };
            }

            var isAllowedByUser = await IsTenantSharedWithUserAsync(bc.UserId, projectId);
            if (!isAllowedByUser) 
            { 
                return new GetContextForProjectResult
                {
                    IsSuccess = false,
                    Error = "access_denied",
                    ContextId = null
                };
            }

            var project = _tenants.GetTenantByID(projectId.Trim());
            if (project == null) 
            {
                return new GetContextForProjectResult
                {
                    IsSuccess = false,
                    Error = "project_not_found",
                    ContextId = null
                };
            }

            var contextId = Guid.NewGuid().ToString("n");
            await _cacheClient.AddStringValueAsync(contextId, projectId, 300);

            return new GetContextForProjectResult
            {
                IsSuccess = true,
                ContextId = contextId,
                Error = null
            };

        }
    }
}
