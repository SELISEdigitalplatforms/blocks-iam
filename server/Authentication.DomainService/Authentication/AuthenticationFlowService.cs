using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.Services;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Security.Claims;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    public class AuthenticationFlowService : IAuthenticationFlowService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly PasswordAuthenticationService _passwordAuthenticationService;
        private readonly SocialAuthorizationService _socialAuthorizationService;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IAuthenticationService _authenticationService;
        private readonly IImpersonationFlowHelper _impersonationFlowHelper;
        private readonly UnifiedTokenSessionService _unifiedTokenSessionService;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<AuthenticationFlowService> _logger;

        public AuthenticationFlowService(
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            IAuditLogRepository auditLogRepo,
            PasswordAuthenticationService passwordAuthenticationService,
            SocialAuthorizationService socialAuthorizationService,
            RefreshTokenAuthenticationService refreshTokenAuthenticationService,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationService authenticationService,
            ICacheClient cacheClient,
            IImpersonationFlowHelper impersonationFlowHelper,
            UnifiedTokenSessionService unifiedTokenSessionService,
            ILogger<AuthenticationFlowService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _auditLogRepo = auditLogRepo;
            _passwordAuthenticationService = passwordAuthenticationService;
            _socialAuthorizationService = socialAuthorizationService;
            _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _authenticationService = authenticationService;
            _impersonationFlowHelper = impersonationFlowHelper;
            _unifiedTokenSessionService = unifiedTokenSessionService;

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

            var response = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);

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

            var useTokensCookie = await ResolveUseTokensCookieAsync(request.ClientId);

            if (useTokensCookie)
            {
                var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
                var tenant = _tenants.GetTenantByID(tenantId);
                var (domain, _, _) = DomainResolver.ResolveDomain(tenant, httpRequest);
                var cookiesSet = AppendCookies(response, httpResponse, domain);
                if (cookiesSet)
                {
                    return new OkObjectResult(new
                    {
                        token_type = response.TokenType,
                        expires_in = response.ExpiresIn,
                        scope = response.Scope,
                        client_id = request.ClientId,
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
                client_id = request.ClientId,
                cookie_set = false
            });
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

        private async Task<bool> ResolveUseTokensCookieAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return true;
            }

            var registration = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            return registration?.UseTokensCookie ?? true;
        }

        private async Task WriteImpersonationAuditEventAsync(HttpRequest httpRequest, string eventType, string userId, string targetTenantId, string severity, string status, string rootTenantId)
        {
            try
            {
                var entry = new AuditLogModel
                {
                    EventType = eventType,
                    UserId = userId,
                    TenantId = rootTenantId,
                    Severity = severity,
                    Status = status,
                    Timestamp = DateTime.UtcNow,
                    IpAddress = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = httpRequest.Headers.UserAgent.ToString(),
                    Details = string.IsNullOrWhiteSpace(targetTenantId) ? null : $"target_tenant={targetTenantId}"
                };

                await _auditLogRepo.CreateAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist impersonation audit event {EventType}", eventType);
            }
        }

        private static bool AppendCookies(TokenResponse response, HttpResponse httpResponse, string domain)
        {
            // Validate response has no error indicator
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return false;
            }

            // Validate access token is present
            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(domain))
            {
                return false;
            }


            var accessCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            DeleteCookie(httpResponse, domain, accessCookieOptions, refreshCookieOptions);

            httpResponse.Cookies.Append(domain, response.AccessToken, accessCookieOptions);

            Console.WriteLine($"Domain: {domain}, AccessTonke: {response.AccessToken}");

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{domain}", response.RefreshToken, refreshCookieOptions);
            }

            return true;
        }

        private static void DeleteCookie(HttpResponse httpResponse, string domain, CookieOptions accessCookieOptions, CookieOptions refreshCookieOptions)
        {

            httpResponse.Cookies.Delete(domain, accessCookieOptions);
            httpResponse.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", refreshCookieOptions);

        }


        public async Task<IActionResult> ExecuteImpersonateAsync(ImpersonateRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            var bc = BlocksContext.GetContext();

            var rootTenant = _tenants.GetTenantByID(bc.TenantId);

            if (rootTenant == null || !rootTenant.IsRootTenant)
            {
                return new ObjectResult(new
                {
                    error = "forbidden",
                    error_description = "Only root-tenant users are allowed to start impersonation"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            if (string.IsNullOrWhiteSpace(request.TargetTenantId))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "target_tenant_id is required" });
            }

            var targetTenant = _tenants.GetTenantByID(request.TargetTenantId);
            if (targetTenant == null)
            {
                return new BadRequestObjectResult(new { error = "invalid_target_tenant", error_description = "Target tenant does not exist" });
            }

            var userId = string.IsNullOrWhiteSpace(bc.UserId) ? request.ImpersontingUserId : bc.UserId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
            }

            var user = await _authenticationRepository.GetUserByIdAsync(userId);
            if (user == null)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
            }

            var isSharedWithUser = await IsTenantSharedWithUserAsync(userId, request.TargetTenantId);
            if (!isSharedWithUser)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_start_denied", userId, request.TargetTenantId, "WARN", "not_shared_with_user", rootTenant.TenantId);
                return new ObjectResult(new
                {
                    error = "forbidden",
                    error_description = "Target tenant is not shared with the requesting user"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            var (rootDomain, rootCookieDomain, _) = DomainResolver.ResolveDomain(rootTenant, httpRequest);
            var rootRefreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
                ? _authenticationService.CookieToken(httpRequest)
                : request.RefreshToken;

            if (string.IsNullOrWhiteSpace(rootRefreshToken))
            {
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            var rootRefreshCacheRaw = await _cacheClient.GetStringValueAsync(rootRefreshToken);
            var rootRefreshCache = string.IsNullOrWhiteSpace(rootRefreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(rootRefreshCacheRaw);

            if (rootRefreshCache == null || rootRefreshCache.ExpiresUtc <= DateTime.UtcNow)
            {
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            // Check for organization switch within existing impersonation
            var existingSessionId = bc.ImpersonationSessionId;

            if (bc.Impersonated)
            {
                var existingSession = await _authenticationRepository.GetImpersonationSessionByIdAsync(existingSessionId);
                if (existingSession != null && existingSession.Status == "active" &&
                    string.Equals(existingSession.TargetTenantId, request.TargetTenantId, StringComparison.OrdinalIgnoreCase))
                {
                    var switchOrgSuccess = await _impersonationFlowHelper.SwitchOrganizationContextAsync(
                        existingSessionId,
                        request.OrganizationId ?? "default");

                    if (switchOrgSuccess)
                    {
                        try
                        {

                            var newTokenRequest = new TokenRequest
                            {
                                GrantType = GrantTypes.Password,
                                ClientId = rootRefreshCache.ClientId,
                                OrganizationId = request.OrganizationId ?? "default",
                                IsImpersonation = true,
                                OriginalTenantId = rootTenant.TenantId,
                                TargetTenantId = request.TargetTenantId,
                                ImpersonatorUserId = userId,
                                Request = httpRequest
                            };

                            var newTokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(newTokenRequest, authConfiguration, user);

                            if (!string.IsNullOrWhiteSpace(newTokenResponse.Error))
                            {
                                _logger.LogError("Token issuance failed during org switch for session {SessionId}. Error: {Error}", existingSessionId, newTokenResponse.Error);
                                return new ObjectResult(new { error = newTokenResponse.Error, error_description = "Failed to issue new tokens after organization switch" })
                                {
                                    StatusCode = StatusCodes.Status500InternalServerError
                                };
                            }

                            await WriteImpersonationAuditEventAsync(httpRequest, "org_switched", userId, request.TargetTenantId, "INFO", "success", rootTenant.TenantId);

                            var cookiesSet = AppendCookies(newTokenResponse, httpResponse, rootDomain);
                            if (cookiesSet)
                            {
                                _logger.LogInformation("Organization switched by user {UserId} in impersonation session {SessionId} to org {OrgId} with new tokens", bc.UserId, existingSessionId, request.OrganizationId);
                                return new OkObjectResult(new ImpersonateResponse { impersonation_mode = true, org_switched = true });
                            }

                            return new OkObjectResult(new
                            {
                                impersonation_mode = true,
                                org_switched = true,
                                access_token = newTokenResponse.AccessToken,
                                refresh_token = newTokenResponse.RefreshToken,
                                token_type = newTokenResponse.TokenType,
                                cookie_set = false
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error during organization switch for session {SessionId}", existingSessionId);
                            return new ObjectResult(new { error = "org_switch_failed", error_description = "An unexpected error occurred during organization switch" })
                            {
                                StatusCode = StatusCodes.Status500InternalServerError
                            };
                        }
                    }
                }
            }

            try
            {

                var clientId = rootRefreshCache.ClientId;
                if (string.IsNullOrWhiteSpace(clientId) || !await HasOidcClientConfigurationAsync(clientId))
                {
                    return new ObjectResult(new
                    {
                        error = "invalid_client",
                        error_description = "Client configuration not found"
                    })
                    {
                        StatusCode = StatusCodes.Status401Unauthorized
                    };
                }

                string sessionId;
                try
                {
                    sessionId = await _impersonationFlowHelper.CreateAndBackupImpersonationSessionAsync(
                        userId,
                        rootTenant.TenantId,
                        request.TargetTenantId,
                        clientId,
                        request.OrganizationId ?? "default");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create impersonation session record for user {UserId}", userId);
                    return new ObjectResult(new { error = "session_creation_failed", error_description = "Failed to create impersonation session" })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError
                    };
                }

                _logger.LogInformation("Impersonation started by user {UserId} from root tenant {RootTenantId} to target tenant {TargetTenantId}", userId, rootTenant.TenantId, request.TargetTenantId);
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_started", userId, request.TargetTenantId, "INFO", "success", rootTenant.TenantId);

                var tokenRequest = new TokenRequest
                {
                    GrantType = GrantTypes.Password,
                    ClientId = clientId,
                    OrganizationId = !string.IsNullOrWhiteSpace(request.OrganizationId)
                        ? request.OrganizationId
                        : (string.IsNullOrWhiteSpace(request.OrganizationId) ? "default" : request.OrganizationId),
                    IsImpersonation = true,
                    OriginalTenantId = rootTenant.TenantId,
                    TargetTenantId = request.TargetTenantId,
                    ImpersonatorUserId = userId,
                    ImpersonationSessionId = sessionId,
                    Request = httpRequest
                };

                

                var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, authConfiguration, user);
                await _unifiedTokenSessionService.RevokeRefreshToken(rootRefreshToken);

                Console.WriteLine($"Before settinCookie AccessToken: {tokenResponse.AccessToken}");

                var cookiesSet = AppendCookies(tokenResponse, httpResponse, rootDomain);

                Console.WriteLine($"IsCookieSet: {cookiesSet}");
                Console.WriteLine($"RoodDomain: {rootDomain}");
                if (!cookiesSet)
                {
                    return new OkObjectResult(new
                    {
                        impersonation_mode = true,
                        access_token = tokenResponse.AccessToken,
                        refresh_token = tokenResponse.RefreshToken,
                        token_type = tokenResponse.TokenType,
                        expires_in = tokenResponse.ExpiresIn,
                        scope = tokenResponse.Scope,
                        id_token = tokenResponse.IdToken,
                        cookie_set = false,
                        impersonation_session_id = sessionId
                    });
                }

               // var sessionCookieOptions = DomainResolver.CreateCookieOptions(rootCookieDomain, rootRefreshCache.ExpiresUtc);

              //  httpResponse.Cookies.Delete(IdpConstants.ImpersonationIdCookieName, sessionCookieOptions);
              //  httpResponse.Cookies.Append(IdpConstants.ImpersonationIdCookieName, sessionId, sessionCookieOptions);

                return new OkObjectResult(new ImpersonateResponse { impersonation_mode = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during impersonation for user {UserId} to tenant {TargetTenantId}", userId, request.TargetTenantId);
                return new ObjectResult(new { error = "impersonation_failed", error_description = "An unexpected error occurred during impersonation" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

        }

        private static void ClearImpersonationCookies(HttpResponse httpResponse, string domain, string cookieDomain)
        {
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(cookieDomain))
            {
                return;
            }
            var options = DomainResolver.CreateCookieOptions(cookieDomain, DateTime.UtcNow.AddDays(-1));
           // httpResponse.Cookies.Delete(IdpConstants.ImpersonationIdCookieName, options);
            httpResponse.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", options);
            httpResponse.Cookies.Delete(domain, options);
        }

        public async Task<IActionResult> ExecuteStopImpersonationAsync(StopImpersonationRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            var bc = BlocksContext.GetContext();
            ImpersonationSession? session = null;
            var rootTenant = _tenants.GetTenantByID(bc.OriginalTenantId);
            var (rootDomain, rootCookieDomain, _) = DomainResolver.ResolveDomain(rootTenant, httpRequest);

            var impersonationSessionId = string.IsNullOrWhiteSpace(request.ImpersonationId)
                ? bc.ImpersonationSessionId
                : request.ImpersonationId;

            if (!string.IsNullOrWhiteSpace(impersonationSessionId))
            {
                session = await _authenticationRepository.GetImpersonationSessionByIdAsync(impersonationSessionId);
            }

            if (session == null)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stop_failed", bc.UserId, null, "WARN", "session_not_found", rootTenant.TenantId);
                ClearImpersonationCookies(httpResponse, rootDomain, rootCookieDomain);
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
                ? _authenticationService.CookieToken(httpRequest)
                : request.RefreshToken;

            if(string.IsNullOrWhiteSpace(refreshToken))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_refresh_token" });
            }

            var rootRefreshCacheRaw = await _cacheClient.GetStringValueAsync(refreshToken);
            var rootRefreshCache = string.IsNullOrWhiteSpace(rootRefreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(rootRefreshCacheRaw);

            if (rootRefreshCache == null || !rootRefreshCache.Impersonated)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_refresh_token" });
            }

            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.Password,
                ClientId = !string.IsNullOrWhiteSpace(session.ClientId) ? session.ClientId : rootRefreshCache.ClientId,
                OrganizationId = session.OrganizationId,
                IsImpersonation = false,
                OriginalTenantId = session.RootTenantId,
                TargetTenantId = session.TargetTenantId,
                ImpersonatorUserId = bc.UserId,
                Request = httpRequest
            };

            // Get root user
            var rootUser = !string.IsNullOrWhiteSpace(bc.UserId) ? await _authenticationRepository.GetUserByIdAsync(bc.UserId) : null;
            if (rootUser == null)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stop_failed", bc.UserId, session.TargetTenantId, "WARN", "root_user_not_found", rootTenant.TenantId);
                ClearImpersonationCookies(httpResponse, rootDomain, rootCookieDomain);
                return new BadRequestObjectResult(new { error = "session_expired" });
            }

            var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, configuration, rootUser);
            if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stop_failed", bc.UserId, session.TargetTenantId, "WARN", tokenResponse.Error, rootTenant.TenantId);
                ClearImpersonationCookies(httpResponse, rootDomain, rootCookieDomain);
                return new BadRequestObjectResult(new { error = tokenResponse.Error });
            }

            try
            {
                var updates = new Dictionary<string, object>
                    {
                        { "status", "ended_by_admin_stop" },
                        { "ended_at", DateTime.UtcNow }
                    };
                await _authenticationRepository.UpdateImpersonationSessionAsync(impersonationSessionId, updates);
               // httpResponse.Cookies.Delete(IdpConstants.ImpersonationIdCookieName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup impersonation session {SessionId}", impersonationSessionId);
            }

            await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stopped", bc.UserId, session.TargetTenantId, "INFO", "success", rootTenant.TenantId);

            await _unifiedTokenSessionService.RevokeRefreshToken(refreshToken);

            var cookiesSet = AppendCookies(tokenResponse, httpResponse, rootDomain);
            if (!cookiesSet)
            {
                return new OkObjectResult(new
                {
                    impersonation_mode = false,
                    access_token = tokenResponse.AccessToken,
                    refresh_token = tokenResponse.RefreshToken,
                    token_type = tokenResponse.TokenType,
                    expires_in = tokenResponse.ExpiresIn,
                    scope = tokenResponse.Scope,
                    id_token = tokenResponse.IdToken,
                    cookie_set = false,
                });
            }
            _logger.LogInformation("Impersonation stopped manually and root session restored");
            return new OkObjectResult(new StopImpersonationResponse { impersonation_mode = false });
        }


    }
}
