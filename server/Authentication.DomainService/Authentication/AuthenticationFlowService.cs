using Blocks.Genesis;
using Blocks.Genesis.Auth;
using DomainService.Dtos;
using DomainService.Oidc.Repositories;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using DomainService.Utilities;
using Iam.DomainService.Accounts;
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

namespace DomainService.Authentication
{
    public class AuthenticationFlowService : IAuthenticationFlowService
    {
        private const string ImpersonationStateCookieName = "impersonation_state";
        private const string RootAccessBackupCookieName = "root_access_token_backup";
        private const string RootRefreshBackupCookieName = "root_refresh_token_backup";
        private const string RootTenantBackupCookieName = "root_tenant_backup";

        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly PasswordAuthenticationService _passwordAuthenticationService;
        private readonly SocialAuthorizationService _socialAuthorizationService;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IAuthenticationService _authenticationService;
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
            _logger = logger;
        }

        public async Task<AuthenticationFlowResult> ExecuteEmbeddedLoginAsync(EmbeddedLoginRequest request, HttpRequest httpRequest)
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

            var user = await _authenticationRepository.GetUserByUsernameAsync(request.Username);
            var resolvedOrganizationId = ResolveSignInOrganizationId(user, request.OrgId);

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.Password,
                Username = request.Username,
                Password = request.Password,
                ClientId = request.ClientId,
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
                Code = request.Code,
                State = request.State,
                ClientId = request.ClientId,
                OrganizationId = request.OrgId,
                Request = httpRequest
            };

            return new AuthenticationFlowResult
            {
                TokenResponse = await _socialAuthorizationService.AuthenticateAsync(tokenRequest, configuration)
            };
        }

        private static string ResolveSignInOrganizationId(User? user, string? requestedOrganizationId)
        {
            if (user == null)
            {
                return string.IsNullOrWhiteSpace(requestedOrganizationId) ? "default" : requestedOrganizationId;
            }

            if (HasOrganizationAccess(user, requestedOrganizationId))
            {
                return requestedOrganizationId!;
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
                ClientId = request.ClientId,
                OrganizationId = request.OrganizationId,
                RefreshToken = refreshToken,
                Request = httpRequest
            };

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
                    error_description = "Impersonation is allowed only for root tenant"
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

            var targetTenant = _tenants.GetTenantByID(request.TargetTenantId);
            if (targetTenant == null)
            {
                return new BadRequestObjectResult(new { error = "invalid_target_tenant", error_description = "Target tenant does not exist" });
            }

            var rootTenant = _tenants.GetTenantByID(rootTenantId);
            if (rootTenant == null)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
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

            if (!CanImpersonateTargetTenant(rootTenantId, request.TargetTenantId, targetTenant))
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_start_denied", userId, request.TargetTenantId, "WARN", "forbidden_target");
                return new ObjectResult(new
                {
                    error = "forbidden",
                    error_description = "Root tenant cannot impersonate this target tenant"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
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

            if (!httpRequest.Cookies.TryGetValue($"{IdpConstants.AccessTokenCookieName}_{rootTenantId}", out var rootAccessToken)
                || !httpRequest.Cookies.TryGetValue($"{IdpConstants.RefreshTokenCookieName}_{rootTenantId}", out var rootRefreshToken)
                || string.IsNullOrWhiteSpace(rootAccessToken)
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

            if (!string.Equals(rootRefreshCache.TenantId, rootTenantId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(rootRefreshCache.AuthMode, "root", StringComparison.OrdinalIgnoreCase))
            {
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (authConfiguration == null)
            {
                return new BadRequestObjectResult(new { error = "auth_config_missing" });
            }

            BackupRootSession(httpResponse, rootAccessToken, rootRefreshToken, rootTenantId, rootTenant.CookieDomain, rootRefreshCache.ExpiresUtc);

            var originalContext = BlocksContext.GetContext();
            try
            {
                SetTenantContextForTokenIssuance(request.TargetTenantId, user);

                var tokenRequest = new TokenRequest
                {
                    GrantType = GrantTypes.Password,
                    ClientId = request.ClientId ?? string.Empty,
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
                    return BuildTokenResponse(httpResponse, tokenResponse);
                }

                var state = new ImpersonationState
                {
                    RootTenantId = rootTenantId,
                    TargetTenantId = request.TargetTenantId,
                    OrgId = tokenRequest.OrganizationId,
                    StartedAtUtc = DateTime.UtcNow
                };

                WriteImpersonationStateCookie(httpResponse, state, rootTenant.CookieDomain, tokenResponse.RefreshExpiresUtc);
                AppendCookies(httpResponse, tokenResponse);

                _logger.LogInformation("Impersonation started by user {UserId} from root tenant {RootTenantId} to target tenant {TargetTenantId}", userId, rootTenantId, request.TargetTenantId);
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_started", userId, request.TargetTenantId, "INFO", "success", rootTenantId);

                return new OkObjectResult(new
                {
                    mode = "impersonation",
                    tenant_id = request.TargetTenantId,
                    status = "started"
                });
            }
            finally
            {
                RestoreOriginalContext(originalContext);
            }
        }

        public async Task<IActionResult> ExecuteStopImpersonationAsync(ClaimsPrincipal principal, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            var cacheClient = httpRequest.HttpContext.RequestServices.GetRequiredService<ICacheClient>();
            string? targetTenantId = null;

            if (TryReadImpersonationState(httpRequest, out var state))
            {
                targetTenantId = state.TargetTenantId;
                var impRefreshCookieName = $"{IdpConstants.RefreshTokenCookieName}_{state.TargetTenantId}";
                if (httpRequest.Cookies.TryGetValue(impRefreshCookieName, out var impRefreshToken) && !string.IsNullOrWhiteSpace(impRefreshToken))
                {
                    await cacheClient.RemoveKeyAsync(impRefreshToken);
                }
            }

            var restored = await TryRestoreRootSessionAsync(httpRequest, httpResponse, "manual_stop");
            if (!restored)
            {
                await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stop_failed", principal.FindFirstValue(BlocksContext.USER_ID_CLAIM), targetTenantId, "WARN", "session_expired");
                return new UnauthorizedObjectResult(new { error = "session_expired" });
            }

            _logger.LogInformation("Impersonation stopped manually and root session restored");
            await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_stopped", principal.FindFirstValue(BlocksContext.USER_ID_CLAIM), targetTenantId, "INFO", "success");
            return new OkObjectResult(new
            {
                mode = "root",
                status = "restored",
                reason = "manual_stop"
            });
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
                if (await TryRestoreRootSessionAsync(httpRequest, httpResponse, "impersonation_expired"))
                {
                    return new OkObjectResult(new { mode = "root", status = "restored", reason = "impersonation_expired" });
                }

                if (TryReadImpersonationState(httpRequest, out _))
                {
                    return new UnauthorizedObjectResult(new { error = "session_expired" });
                }

                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
            }

            var tokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(cachedRefreshToken);
            if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
            {
                if (await TryRestoreRootSessionAsync(httpRequest, httpResponse, "impersonation_expired"))
                {
                    return new OkObjectResult(new { mode = "root", status = "restored", reason = "impersonation_expired" });
                }

                if (TryReadImpersonationState(httpRequest, out _))
                {
                    return new UnauthorizedObjectResult(new { error = "session_expired" });
                }

                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
            }

            var currentTenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrWhiteSpace(currentTenantId))
            {
                return new UnauthorizedObjectResult(new { error = "tenant_not_resolved" });
            }

            if (!string.Equals(tokenCache.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token tenant mismatch" });
            }

            var hasImpersonationState = TryReadImpersonationState(httpRequest, out var impersonationState);
            if (hasImpersonationState)
            {
                var validImpersonationChain = string.Equals(tokenCache.AuthMode, "impersonation", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(tokenCache.TargetTenantId, impersonationState.TargetTenantId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(tokenCache.OriginalTenantId, impersonationState.RootTenantId, StringComparison.OrdinalIgnoreCase);

                if (!validImpersonationChain)
                {
                    return new UnauthorizedObjectResult(new { error = "session_expired" });
                }
            }

            var user = await _authenticationRepository.GetUserByIdAsync(tokenCache.UserId);
            if (user == null)
            {
                return new UnauthorizedObjectResult(new { error = "invalid_user" });
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.RefreshToken,
                ClientId = request.ClientId ?? string.Empty,
                OrganizationId = request.OrganizationId ?? "default",
                RefreshToken = refreshToken,
                IsImpersonation = string.Equals(tokenCache.AuthMode, "impersonation", StringComparison.OrdinalIgnoreCase),
                OriginalTenantId = tokenCache.OriginalTenantId,
                TargetTenantId = tokenCache.TargetTenantId,
                ImpersonatorUserId = tokenCache.ImpersonatorUserId,
                Request = httpRequest
            };

            var result = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);

            if (hasImpersonationState)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    return BuildTokenResponse(httpResponse, result);
                }

                AppendCookies(httpResponse, result);
                return new OkObjectResult(new
                {
                    mode = "impersonation",
                    status = "refreshed",
                    access_token = result.AccessToken,
                    refresh_token = result.RefreshToken,
                    token_type = result.TokenType,
                    expires_in = result.ExpiresIn,
                    expires_utc = result.ExpiresUtc,
                    refresh_expires_utc = result.RefreshExpiresUtc,
                    scope = result.Scope,
                    id_token = result.IdToken
                });
            }

            return BuildTokenResponse(httpResponse, result);
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

        private async Task<bool> TryRestoreRootSessionAsync(HttpRequest httpRequest, HttpResponse httpResponse, string reason)
        {
            if (!TryReadImpersonationState(httpRequest, out var state))
            {
                return false;
            }

            if (!httpRequest.Cookies.TryGetValue(RootAccessBackupCookieName, out var rootAccessToken)
                || !httpRequest.Cookies.TryGetValue(RootRefreshBackupCookieName, out var rootRefreshToken)
                || !httpRequest.Cookies.TryGetValue(RootTenantBackupCookieName, out var rootTenantId)
                || string.IsNullOrWhiteSpace(rootAccessToken)
                || string.IsNullOrWhiteSpace(rootRefreshToken)
                || string.IsNullOrWhiteSpace(rootTenantId))
            {
                return false;
            }

            var rootTenant = _tenants.GetTenantByID(rootTenantId);
            if (rootTenant == null)
            {
                return false;
            }

            var cacheClient = httpRequest.HttpContext.RequestServices.GetRequiredService<ICacheClient>();
            var refreshCacheRaw = await cacheClient.GetStringValueAsync(rootRefreshToken);
            var refreshCache = string.IsNullOrWhiteSpace(refreshCacheRaw)
                ? null
                : JsonSerializer.Deserialize<RefreshTokenCache>(refreshCacheRaw);

            if (refreshCache == null || refreshCache.ExpiresUtc <= DateTime.UtcNow)
            {
                return false;
            }

            var accessExpiry = GetJwtExpiryUtc(rootAccessToken) ?? DateTime.UtcNow.AddMinutes(15);
            httpResponse.Cookies.Append($"{IdpConstants.AccessTokenCookieName}_{rootTenantId}", rootAccessToken, CreateCookieOptions(rootTenant.CookieDomain, accessExpiry));
            httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{rootTenantId}", rootRefreshToken, CreateCookieOptions(rootTenant.CookieDomain, refreshCache.ExpiresUtc));

            ClearImpersonationCookies(httpResponse, state, rootTenant.CookieDomain);
            _logger.LogInformation("Impersonation session restored to root tenant {RootTenantId} due to {Reason}", rootTenantId, reason);
            await WriteImpersonationAuditEventAsync(httpRequest, "impersonation_restored", refreshCache.ImpersonatorUserId ?? refreshCache.UserId, state.TargetTenantId, "INFO", reason, rootTenantId);

            return true;
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

        private static void BackupRootSession(HttpResponse httpResponse, string rootAccessToken, string rootRefreshToken, string rootTenantId, string? cookieDomain, DateTime refreshExpiresUtc)
        {
            var accessExpiry = GetJwtExpiryUtc(rootAccessToken) ?? DateTime.UtcNow.AddMinutes(15);

            httpResponse.Cookies.Append(RootAccessBackupCookieName, rootAccessToken, CreateCookieOptions(cookieDomain, accessExpiry));
            httpResponse.Cookies.Append(RootRefreshBackupCookieName, rootRefreshToken, CreateCookieOptions(cookieDomain, refreshExpiresUtc));
            httpResponse.Cookies.Append(RootTenantBackupCookieName, rootTenantId, CreateCookieOptions(cookieDomain, refreshExpiresUtc));
        }

        private static void WriteImpersonationStateCookie(HttpResponse httpResponse, ImpersonationState state, string? cookieDomain, DateTime expiresUtc)
        {
            var json = JsonSerializer.Serialize(state);
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            httpResponse.Cookies.Append(ImpersonationStateCookieName, encoded, CreateCookieOptions(cookieDomain, expiresUtc));
        }

        private static bool TryReadImpersonationState(HttpRequest httpRequest, out ImpersonationState state)
        {
            state = new ImpersonationState();

            if (!httpRequest.Cookies.TryGetValue(ImpersonationStateCookieName, out var encoded) || string.IsNullOrWhiteSpace(encoded))
            {
                return false;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var parsed = JsonSerializer.Deserialize<ImpersonationState>(json);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.RootTenantId) || string.IsNullOrWhiteSpace(parsed.TargetTenantId))
                {
                    return false;
                }

                state = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ClearImpersonationCookies(HttpResponse httpResponse, ImpersonationState state, string? rootCookieDomain)
        {
            DeleteCookie(httpResponse, ImpersonationStateCookieName, rootCookieDomain);
            DeleteCookie(httpResponse, RootAccessBackupCookieName, rootCookieDomain);
            DeleteCookie(httpResponse, RootRefreshBackupCookieName, rootCookieDomain);
            DeleteCookie(httpResponse, RootTenantBackupCookieName, rootCookieDomain);
            DeleteCookie(httpResponse, $"{IdpConstants.AccessTokenCookieName}_{state.TargetTenantId}", rootCookieDomain);
            DeleteCookie(httpResponse, $"{IdpConstants.RefreshTokenCookieName}_{state.TargetTenantId}", rootCookieDomain);
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

        private static void SetTenantContextForTokenIssuance(string tenantId, User user)
        {
            var createMethods = typeof(BlocksContext)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(method => method.Name == "Create" && method.ReturnType == typeof(BlocksContext))
                .ToList();

            var create15Method = createMethods.FirstOrDefault(method => method.GetParameters().Length == 15);
            if (create15Method != null)
            {
                var context = (BlocksContext)create15Method.Invoke(null, new object[]
                {
                    tenantId,
                    Array.Empty<string>(),
                    user.ItemId,
                    true,
                    string.Empty,
                    string.Empty,
                    DateTime.UtcNow.AddHours(1),
                    user.Email ?? string.Empty,
                    Array.Empty<string>(),
                    user.UserName ?? string.Empty,
                    string.Empty,
                    $"{user.FirstName} {user.LastName}".Trim(),
                    string.Empty,
                    string.Empty,
                    tenantId
                })!;

                BlocksContext.SetContext(context, true);
                return;
            }

            var create14Method = createMethods.FirstOrDefault(method => method.GetParameters().Length == 14);
            if (create14Method != null)
            {
                var context = (BlocksContext)create14Method.Invoke(null, new object[]
                {
                    tenantId,
                    Array.Empty<string>(),
                    user.ItemId,
                    true,
                    string.Empty,
                    string.Empty,
                    DateTime.UtcNow.AddHours(1),
                    user.Email ?? string.Empty,
                    Array.Empty<string>(),
                    user.UserName ?? string.Empty,
                    string.Empty,
                    $"{user.FirstName} {user.LastName}".Trim(),
                    string.Empty,
                    tenantId
                })!;

                BlocksContext.SetContext(context, true);
            }
        }

        private static void RestoreOriginalContext(BlocksContext? originalContext)
        {
            if (originalContext == null)
            {
                BlocksContext.ClearContext();
                return;
            }

            BlocksContext.SetContext(originalContext, true);
        }

        private IActionResult BuildTokenResponse(HttpResponse httpResponse, TokenResponse response)
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

            AppendCookies(httpResponse, response);
            return new OkObjectResult(new
            {
                access_token = response.AccessToken,
                refresh_token = response.RefreshToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                expires_utc = response.ExpiresUtc,
                refresh_expires_utc = response.RefreshExpiresUtc,
                scope = response.Scope,
                id_token = response.IdToken
            });
        }

        private static void AppendCookies(HttpResponse httpResponse, TokenResponse response)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
            var accessCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            if (!string.IsNullOrWhiteSpace(response.AccessToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.AccessTokenCookieName}_{tenantId}", response.AccessToken, accessCookieOptions);
            }

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{tenantId}", response.RefreshToken, refreshCookieOptions);
            }
        }

        private static CookieOptions CreateCookieOptions(string? domain, DateTime expiresUtc)
        {
            return new CookieOptions
            {
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Expires = expiresUtc == default ? DateTime.UtcNow.AddHours(1) : expiresUtc
            };
        }
    }
}
