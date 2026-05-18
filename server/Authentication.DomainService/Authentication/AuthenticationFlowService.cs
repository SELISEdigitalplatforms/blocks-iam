using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.ResponseModel;
using Authentication.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    public class AuthenticationFlowService : IAuthenticationFlowService
    {
        private const string ImpersonationIdCookieName = "impersonation_session_id";
        private const string IdpSessionCookieName = "idp_session_id";

        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly PasswordAuthenticationService _passwordAuthenticationService;
        private readonly SocialAuthorizationService _socialAuthorizationService;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IAuthenticationService _authenticationService;
        private readonly IIdpSessionService _idpSessionService;
        private readonly IImpersonationBackupService _impersonationBackupService;
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
            IIdpSessionService idpSessionService,
            IImpersonationBackupService impersonationBackupService,
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
            _idpSessionService = idpSessionService;
            _impersonationBackupService = impersonationBackupService;
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

            var cacheClient = httpRequest.HttpContext.RequestServices.GetRequiredService<ICacheClient>();
            var refreshCacheRaw = await cacheClient.GetStringValueAsync(refreshToken);
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

        public async Task<IActionResult> ExecuteImpersonateAsync(ImpersonationRequest request, ClaimsPrincipal principal, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            if (!IsCurrentTenantRoot(principal))
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

            var rootTenantId = GetCurrentTenantId(principal);
            if (string.IsNullOrWhiteSpace(rootTenantId))
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
            }

            var rootTenant = _tenants.GetTenantByID(rootTenantId);
            if (rootTenant == null || !rootTenant.IsRootTenant)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
            }

            var targetTenant = _tenants.GetTenantByID(request.TargetTenantId);
            if (targetTenant == null)
            {
                return new BadRequestObjectResult(new { error = "invalid_target_tenant", error_description = "Target tenant does not exist" });
            }

            var userId = principal.FindFirstValue(BlocksContext.USER_ID_CLAIM) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
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
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_start_denied", userId, request.TargetTenantId, "WARN", "not_shared_with_user");
                return new ObjectResult(new
                {
                    error = "forbidden",
                    error_description = "Target tenant is not shared with the requesting user"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            var (rootDomain, rootCookieDomain, isRootDomainResolved) = DomainResolver.ResolveDomain(rootTenant, httpRequest);
            if (!isRootDomainResolved || string.IsNullOrWhiteSpace(rootDomain))
            {
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            if (!httpRequest.Cookies.TryGetValue($"{IdpConstants.RefreshTokenCookieName}_{rootDomain}", out var rootRefreshToken)
                || string.IsNullOrWhiteSpace(rootRefreshToken))
            {
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            var cacheClient = httpRequest.HttpContext.RequestServices.GetRequiredService<ICacheClient>();
            var rootRefreshCacheRaw = await cacheClient.GetStringValueAsync(rootRefreshToken);
            var rootRefreshCache = string.IsNullOrWhiteSpace(rootRefreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(rootRefreshCacheRaw);

            if (rootRefreshCache == null || rootRefreshCache.ExpiresUtc <= DateTime.UtcNow)
            {
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }
            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            // Check for organization switch within existing impersonation
            if (httpRequest.Cookies.TryGetValue(ImpersonationIdCookieName, out var existingSessionId) && !string.IsNullOrWhiteSpace(existingSessionId))
            {
                var existingSession = await _authenticationRepository.GetImpersonationSessionByIdAsync(existingSessionId);
                if (existingSession != null && existingSession.Status == "active" && 
                    string.Equals(existingSession.TargetTenantId, request.TargetTenantId, StringComparison.OrdinalIgnoreCase))
                {
                    // This is an organization switch within the same impersonation
                    // STANDARD: Issue new tokens with updated org_id claim
                    var switchOrgSuccess = await ImpersonationFlowHelper.SwitchOrganizationContextAsync(
                        existingSessionId,
                        request.OrganizationId ?? "default",
                        _authenticationRepository);

                    if (switchOrgSuccess)
                    {
                        // Tenant impersonation: same user, different tenant context + new org
                        // Must set tenant context before token issuance (same as normal impersonation start)
                        var orgSwitchContext = BlocksContext.GetContext();
                        try
                        {

                            var newTokenRequest = new TokenRequest
                            {
                                GrantType = GrantTypes.Password,
                                ClientId = rootRefreshCache.ClientId,
                                OrganizationId = request.OrganizationId ?? "default",
                                IsImpersonation = true,
                                OriginalTenantId = rootTenantId,
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

                            await WriteImpersonationAuditEventAsync(httpRequest, "org_switched", rootTenantId, request.TargetTenantId, "INFO", "success", rootTenantId);

                            var cookiesSet = AppendCookies(httpResponse, newTokenResponse, targetTenant, httpRequest);
                            if (cookiesSet)
                            {
                                _logger.LogInformation("Organization switched by user {UserId} in impersonation session {SessionId} to org {OrgId} with new tokens", rootTenantId, existingSessionId, request.OrganizationId);
                                return new OkObjectResult(new ImpersonateResponse { ImpersonationMode = true });
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
                        finally
                        {
                            BlocksContext.SetContext(orgSwitchContext);
                        }
                    }
                }
            }

            var originalContext = BlocksContext.GetContext();
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

                var tokenRequest = new TokenRequest
                {
                    GrantType = GrantTypes.Password,
                    ClientId = clientId,
                    OrganizationId = !string.IsNullOrWhiteSpace(request.OrganizationId)
                        ? request.OrganizationId
                        : (string.IsNullOrWhiteSpace(request.OrgId) ? "default" : request.OrgId),
                    IsImpersonation = true,
                    OriginalTenantId = rootTenantId,
                    TargetTenantId = request.TargetTenantId,
                    ImpersonatorUserId = userId,
                    Request = httpRequest
                };

                var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, authConfiguration, user);
                if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
                {
                    return await BuildTokenResponseAsync(httpResponse, tokenResponse);
                }

                var cookiesSet = AppendCookies(httpResponse, tokenResponse, targetTenant, httpRequest);
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
                        cookie_set = false
                    });
                }

                // Phase 2: Create impersonation session and backup root token
                string sessionId;
                try
                {
                    sessionId = await ImpersonationFlowHelper.CreateAndBackupImpersonationSessionAsync(
                        userId,
                        rootTenantId,
                        request.TargetTenantId,
                        request.OrganizationId ?? "default",
                        rootRefreshToken,
                        rootRefreshCache.ExpiresUtc,
                        _authenticationRepository,
                        _impersonationBackupService);

                    // Write impersonation session ID cookie
                    var sessionCookieOptions = CreateCookieOptions(rootCookieDomain, tokenResponse.RefreshExpiresUtc);
                    httpResponse.Cookies.Append(ImpersonationIdCookieName, sessionId, sessionCookieOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create impersonation session record for user {UserId}", userId);
                    return new ObjectResult(new { error = "session_creation_failed", error_description = "Failed to create impersonation session" })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError
                    };
                }

                _logger.LogInformation("Impersonation started by user {UserId} from root tenant {RootTenantId} to target tenant {TargetTenantId}", userId, rootTenantId, request.TargetTenantId);
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_started", userId, request.TargetTenantId, "INFO", "success", rootTenantId);

                return new OkObjectResult(new ImpersonateResponse { ImpersonationMode = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during impersonation for user {UserId} to tenant {TargetTenantId}", principal.Identity?.Name, request.TargetTenantId);
                return new ObjectResult(new { error = "impersonation_failed", error_description = "An unexpected error occurred during impersonation" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
            
        }

        public async Task<IActionResult> ExecuteStopImpersonationAsync(ClaimsPrincipal principal, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            string? targetTenantId = null;
            string? impersonationSessionId = null;
            string? rootRefreshToken = null;
            string? rootUserId = null;
            string? rootTenantId = null;
            var userId = BlocksContext.GetContext()?.UserId;

            // Try to get impersonation session id from cookie
            if (httpRequest.Cookies.TryGetValue(ImpersonationIdCookieName, out var sessionId) && !string.IsNullOrWhiteSpace(sessionId))
            {
                impersonationSessionId = sessionId;
                var session = await _authenticationRepository.GetImpersonationSessionByIdAsync(sessionId);
                if (session != null)
                {
                    targetTenantId = session.TargetTenantId;
                    // Try to get backup root refresh token from cache
                    var backup = await _impersonationBackupService.GetBackupTokenAsync(sessionId);
                    if (backup != null && !string.IsNullOrWhiteSpace(backup.RefreshToken))
                    {
                        rootRefreshToken = backup.RefreshToken;
                        rootUserId = BlocksContext.GetContext()?.UserId;
                        rootTenantId = session.RootTenantId;
                    }
                }
            }

            // If not found in cache, delete cookie and trigger logout
            if (string.IsNullOrWhiteSpace(rootRefreshToken))
            {
                // Remove impersonation cookie
                if (!string.IsNullOrWhiteSpace(impersonationSessionId))
                {
                    httpResponse.Cookies.Delete(ImpersonationIdCookieName);
                }
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stop_failed", userId, targetTenantId, "WARN", "session_expired");
                var (domain, rootCookieDomain) = ResolveDomainForClearImpersonation(httpRequest, rootTenantId);
                ClearImpersonationCookies(httpResponse, domain, rootCookieDomain);
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            // Restore root session: issue new token for root user (no impersonation)
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();

            // Compose new token request for root user
            var cacheClient = httpRequest.HttpContext.RequestServices.GetRequiredService<ICacheClient>();
            var rootRefreshCacheRaw = await cacheClient.GetStringValueAsync(rootRefreshToken);
            var rootRefreshCache = string.IsNullOrWhiteSpace(rootRefreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(rootRefreshCacheRaw);

            if (rootRefreshCache == null || rootRefreshCache.ExpiresUtc <= DateTime.UtcNow)
            {
                var (domain, rootCookieDomain) = ResolveDomainForClearImpersonation(httpRequest, rootTenantId);
                ClearImpersonationCookies(httpResponse, domain, rootCookieDomain); 
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.RefreshToken,
                ClientId = rootRefreshCache.ClientId,
                RefreshToken = rootRefreshToken,
                Request = httpRequest
            };

            // Get root user
            var rootUser = !string.IsNullOrWhiteSpace(rootUserId) ? await _authenticationRepository.GetUserByIdAsync(rootUserId) : null;
            if (rootUser == null)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stop_failed", userId, targetTenantId, "WARN", "root_user_not_found");
                var (domain, rootCookieDomain) = ResolveDomainForClearImpersonation(httpRequest, rootTenantId);
                ClearImpersonationCookies(httpResponse, domain, rootCookieDomain); 
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, configuration, rootUser);
            if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stop_failed", userId, targetTenantId, "WARN", tokenResponse.Error);
                var (domain, rootCookieDomain) = ResolveDomainForClearImpersonation(httpRequest, rootTenantId);
                ClearImpersonationCookies(httpResponse, domain, rootCookieDomain); 
                return new UnauthorizedObjectResult(new { error = tokenResponse.Error });
            }

            // Set new cookies for root user (no impersonation)
            var rootTenant = !string.IsNullOrWhiteSpace(rootTenantId) ? _tenants.GetTenantByID(rootTenantId) : null;
            var cookiesSet = AppendCookies(httpResponse, tokenResponse, rootTenant, httpRequest);

            // Clean up impersonation session and backup
            if (!string.IsNullOrWhiteSpace(impersonationSessionId))
            {
                try
                {
                    var updates = new Dictionary<string, object>
                    {
                        { "status", "ended_by_admin_stop" },
                        { "ended_at", DateTime.UtcNow }
                    };
                    await _authenticationRepository.UpdateImpersonationSessionAsync(impersonationSessionId, updates);
                    await _impersonationBackupService.DeleteBackupTokenAsync(impersonationSessionId);
                    httpResponse.Cookies.Delete(ImpersonationIdCookieName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup impersonation session {SessionId}", impersonationSessionId);
                }
            }

            _logger.LogInformation("Impersonation stopped manually and root session restored");
            await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stopped", userId, targetTenantId, "INFO", "success");
            return new OkObjectResult(new StopImpersonationResponse { ImpersonationMode = false });
        }

        private (string? domain, string? rootCookieDomain) ResolveDomainForClearImpersonation(HttpRequest httpRequest, string? tenantId)
        {
            Tenant? tenant = null;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                tenant = _tenants.GetTenantByID(tenantId);
            }
            var (domain, rootCookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, httpRequest);
            return (domain, rootCookieDomain);
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

            var cacheClient = httpRequest.HttpContext.RequestServices.GetRequiredService<ICacheClient>();
            var cachedRefreshToken = await cacheClient.GetStringValueAsync(refreshToken);
            if (string.IsNullOrWhiteSpace(cachedRefreshToken))
            {
                await HandlePotentialRefreshTokenReuseAsync(refreshToken, cacheClient);
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

            httpRequest.Cookies.TryGetValue(ImpersonationIdCookieName, out var impersonationId);
            var hasImpersonationCookie = !string.IsNullOrWhiteSpace(impersonationId);
            if (hasImpersonationCookie)
            {
                return new UnauthorizedObjectResult(new { error = "session_expired" });
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
                IsImpersonation = hasImpersonationCookie,
                OriginalTenantId = tokenCache.OriginalTenantId,
                TargetTenantId = tokenCache.TargetTenantId,
                ImpersonatorUserId = tokenCache.ImpersonatorUserId,
                Request = httpRequest
            };

            var result = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);

            if (hasImpersonationCookie)
            {
                try
                {
                    var backupToken = await _impersonationBackupService.GetBackupTokenAsync(impersonationId);
                    if (backupToken != null && backupToken.ExpiresUtc > DateTime.UtcNow)
                    {
                        var gracePeriodMinutes = configuration.TokenRotationGracePeriodMinutes;
                        var rotationThreshold = backupToken.ExpiresUtc.AddMinutes(-gracePeriodMinutes);
                        
                        if (DateTime.UtcNow >= rotationThreshold)
                        {
                            // Attempt to rotate backup root token
                            var (rotationSuccess, newRefreshToken, newExpiresUtc, rotationErrorCode) = await ImpersonationFlowHelper.RotateBackupRootTokenAsync(
                                impersonationId,
                                _impersonationBackupService,
                                async (token) =>
                                {
                                    var rootTokenRequest = new TokenRequest
                                    {
                                        GrantType = GrantTypes.RefreshToken,
                                        ClientId = tokenCache.ClientId,
                                        RefreshToken = token,
                                        Request = httpRequest
                                    };
                                    var rootUser = await _authenticationRepository.GetUserByIdAsync(tokenCache.UserId);
                                    var rotationResult = await _refreshTokenAuthenticationService.AuthenticateAsync(rootTokenRequest, configuration, rootUser);
                                    return (rotationResult.AccessToken ?? string.Empty, rotationResult.RefreshToken ?? string.Empty, rotationResult.RefreshExpiresUtc);
                                },
                                configuration,
                                _logger);
                            
                            if (rotationSuccess)
                            {
                                _logger.LogInformation("Backup root token rotated during impersonation refresh for session {SessionId}", impersonationId);
                            }
                            else
                            {
                                // Log specific error code for monitoring/alerts
                                _logger.LogWarning("Backup root token rotation failed for session {SessionId}. Error: {ErrorCode}", impersonationId, rotationErrorCode);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to rotate backup root token for session {SessionId}, but continuing with impersonation refresh", impersonationId);
                    // Don't fail the refresh - backup rotation is not critical to the refresh flow
                }

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    return await BuildTokenResponseAsync(httpResponse, result);
                }

                var clientId = TryGetClientIdFromAccessToken(result.AccessToken) ?? tokenCache.ClientId;
                var useTokensCookie = await ResolveUseTokensCookieAsync(clientId);

                if (useTokensCookie)
                {
                    var currentTenant = _tenants.GetTenantByID(currentTenantId);
                    var cookiesSet = AppendCookies(httpResponse, result, currentTenant, httpRequest);
                    if (cookiesSet)
                    {
                        return new OkObjectResult(new
                        {
                            mode = "impersonation",
                            status = "refreshed",
                            token_type = result.TokenType,
                            expires_in = result.ExpiresIn,
                            scope = result.Scope,
                        });
                    }
                }

                return new OkObjectResult(new
                {
                    mode = "impersonation",
                    status = "refreshed",
                    access_token = result.AccessToken,
                    refresh_token = result.RefreshToken,
                    token_type = result.TokenType,
                    expires_in = result.ExpiresIn,
                    scope = result.Scope,
                    id_token = result.IdToken,
                });
            }

            return await BuildTokenResponseAsync(httpResponse, result);
        }

        private async Task HandlePotentialRefreshTokenReuseAsync(string refreshToken, ICacheClient cacheClient)
        {
            await _authenticationRepository.RevokeIdentitySessionsByRefreshTokensAsync(new List<string> { refreshToken });

            await cacheClient.RemoveKeyAsync(refreshToken);
            _logger.LogWarning("Potential refresh token reuse detected for token {RefreshToken}. Existing session revoked.", refreshToken);
        }

        private bool IsCurrentTenantRoot(ClaimsPrincipal principal)
        {
            var tenantId = GetCurrentTenantId(principal);
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return false;
            }

            return _tenants.GetTenantByID(tenantId)?.IsRootTenant ?? false;
        }

        private static string? GetCurrentTenantId(ClaimsPrincipal principal)
        {
            return principal.FindFirstValue(BlocksContext.TENANT_ID_CLAIM)
                ?? BlocksContext.GetContext()?.TenantId;
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

        private static bool CanImpersonateTargetTenant(string rootTenantId, string targetTenantId, Tenant targetTenant)
        {
            if (string.Equals(rootTenantId, targetTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (targetTenant.IsRootTenant)
            {
                return false;
            }

            return true;
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

        private async Task WriteImpersonationAuditEventAsync(HttpRequest httpRequest, string eventType, string? userId, string? targetTenantId, string severity, string status, string? rootTenantId = null)
        {
            try
            {
                var entry = new AuditLogModel
                {
                    EventType = eventType,
                    UserId = userId,
                    TenantId = rootTenantId ?? BlocksContext.GetContext()?.TenantId,
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

        private static void ClearImpersonationCookies(HttpResponse httpResponse, string? domain, string? rootCookieDomain)
        {
            DeleteCookie(httpResponse, ImpersonationIdCookieName, rootCookieDomain);
            DeleteCookie(httpResponse, $"{domain}", rootCookieDomain);
            DeleteCookie(httpResponse, $"{IdpConstants.RefreshTokenCookieName}_{domain}", rootCookieDomain);
        }

        private static void DeleteCookie(HttpResponse httpResponse, string cookieName, string? domain)
        {
            httpResponse.Cookies.Delete(cookieName, new CookieOptions
            {
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                Path = "/",
                Secure = true,
                SameSite = SameSiteMode.None
            });

            if (!string.IsNullOrWhiteSpace(domain))
            {
                httpResponse.Cookies.Delete(cookieName, new CookieOptions
                {
                    Path = "/",
                    Secure = true,
                    SameSite = SameSiteMode.None
                });
            }
        }

        private static DateTime? GetJwtExpiryUtc(string jwtToken)
        {
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(jwtToken);
                var exp = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
                return long.TryParse(exp, out var expSeconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime
                    : null;
            }
            catch
            {
                return null;
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

        private static bool IsRootDatabaseToken(string? grantType)
        {
            if (string.IsNullOrWhiteSpace(grantType))
            {
                return false;
            }

            return string.Equals(grantType, GrantTypes.Password, StringComparison.OrdinalIgnoreCase)
                || string.Equals(grantType, GrantTypes.MfaCode, StringComparison.OrdinalIgnoreCase);
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
            ClearImpersonationCookies(httpResponse, tokenDomain, response.CookieDomain);
            if (!isResolved || string.IsNullOrWhiteSpace(tokenDomain))
            {
                return false;
            }
            var accessCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            httpResponse.Cookies.Append($"{tokenDomain}", response.AccessToken, accessCookieOptions);

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{tokenDomain}", response.RefreshToken, refreshCookieOptions);
            }

            return true;
        }

        private static DateTime GetIdpSessionAbsoluteExpiryUtc()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_DAYS");
            if (double.TryParse(configured, out var days) && days > 0 && days <= 365)
            {
                return DateTime.UtcNow.AddDays(days);
            }

            return DateTime.UtcNow.AddDays(30);
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

    }
}
