using System.Security.Claims;
using Api.Controllers;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using DomainService.Authentication;
using DomainService.Dtos;
using DomainService.Oidc.Repositories;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using DomainService.Utilities;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthenticationController : ControllerBase
{
    private const string ImpersonationStateCookieName = "impersonation_state";
    private const string RootAccessBackupCookieName = "root_access_token_backup";
    private const string RootRefreshBackupCookieName = "root_refresh_token_backup";
    private const string RootTenantBackupCookieName = "root_tenant_backup";

    private readonly IAuthenticationService _authenticationService;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly ITenants _tenants;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IAccountService _accountService;
    private readonly ChangeControllerContext _changeControllerContext;
    private readonly PasswordAuthenticationService _passwordAuthenticationService;
    private readonly SocialAuthorizationService _socialAuthorizationService;
    private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
    private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
    private readonly ILogger<AuthenticationController> _logger;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        IConfiguration configuration,
        IAuthenticationDomainService domainService,
        IAuthenticationRepository authenticationRepository,
        ITenants tenants,
        IAuditLogRepository auditLogRepo,
        IAccountService accountService,
        ChangeControllerContext changeControllerContext,
        IConfigurationService cloudConfigurationService,
        PasswordAuthenticationService passwordAuthenticationService,
        SocialAuthorizationService socialAuthorizationService,
        RefreshTokenAuthenticationService refreshTokenAuthenticationService,
        IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
        ILogger<AuthenticationController> logger)
    {
        _authenticationService = authenticationService;
        _authenticationRepository = authenticationRepository;
        _tenants = tenants;
        _auditLogRepo = auditLogRepo;
        _accountService = accountService;
        _changeControllerContext = changeControllerContext;
        _passwordAuthenticationService = passwordAuthenticationService;
        _socialAuthorizationService = socialAuthorizationService;
        _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
        _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
        _logger = logger;
    }

    [HttpPost("recover")]
    [AllowAnonymous]
    public async Task<IActionResult> Recover([FromBody] RecoveryUserRequest command)
    {
        _changeControllerContext.ChangeContext(command);
        var result = await _accountService.RecoverAccountAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest command)
    {
        _changeControllerContext.ChangeContext(command);
        var result = await _accountService.ResetAccountPasswordAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest command)
    {
        _changeControllerContext.ChangeContext(command);
        var result = await _accountService.ChangePasswordAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] EmbeddedLoginRequest request)
    {
        var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
        if (configuration == null)
        {
            return BadRequest(new { error = "auth_config_missing" });
        }

        var tokenRequest = new TokenRequest
        {
            GrantType = GrantTypes.Password,
            Username = request.Username,
            Password = request.Password,
            ClientId = request.ClientId,
            OrganizationId = request.OrganizationId ?? "default",
            Request = Request
        };

        var result = await _passwordAuthenticationService.AuthenticateAsync(tokenRequest, configuration);
        return BuildTokenResponse(result);
    }

    [HttpPost("social-login")]
    [AllowAnonymous]
    public async Task<IActionResult> SocialLogin([FromBody] SocialLoginRequest request)
    {
        var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
        if (configuration == null)
        {
            return BadRequest(new { error = "auth_config_missing" });
        }

        var tokenRequest = new TokenRequest
        {
            GrantType = GrantTypes.Social,
            Code = request.Code,
            State = request.State,
            ClientId = request.ClientId,
            OrganizationId = request.OrganizationId ?? "default",
            Request = Request
        };

        var result = await _socialAuthorizationService.AuthenticateAsync(tokenRequest, configuration);
        return BuildTokenResponse(result);
    }

    [HttpPost("switch-org")]
    [Authorize]
    public async Task<IActionResult> SwitchOrg([FromBody] SwitchOrganizationRequest request)
    {
        var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
        if (configuration == null)
        {
            return BadRequest(new { error = "auth_config_missing" });
        }

        if (!IsCurrentTenantRoot())
        {
            await WriteImpersonationAuditEventAsync("impersonation_start_denied", userId: User.FindFirstValue(BlocksContext.USER_ID_CLAIM), targetTenantId: request.OrganizationId, severity: "WARN", status: "forbidden");
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "forbidden",
                error_description = "Impersonation is allowed only for root tenant"
            });
        }

        var userId = User.FindFirstValue(BlocksContext.USER_ID_CLAIM) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "invalid_user" });
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
            return BadRequest(new { error = "organization_not_available" });
        }

        var tokenRequest = new TokenRequest
        {
            GrantType = GrantTypes.RefreshToken,
            ClientId = request.ClientId,
            OrganizationId = request.OrganizationId,
            Request = Request
        };

        var result = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);
        return BuildTokenResponse(result);
    }

    [HttpPost("impersonate")]
    [Authorize]
    public async Task<IActionResult> Impersonate([FromBody] ImpersonationRequest request)
    {
        if (!IsCurrentTenantRoot())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "forbidden",
                error_description = "Impersonation is allowed only for root tenant"
            });
        }

        if (string.IsNullOrWhiteSpace(request.TargetTenantId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "target_tenant_id is required" });
        }

        var rootTenantId = GetCurrentTenantId();
        if (string.IsNullOrWhiteSpace(rootTenantId))
        {
            return Unauthorized(new { error = "invalid_user" });
        }

        var targetTenant = _tenants.GetTenantByID(request.TargetTenantId);
        if (targetTenant == null)
        {
            return BadRequest(new { error = "invalid_target_tenant", error_description = "Target tenant does not exist" });
        }

        var rootTenant = _tenants.GetTenantByID(rootTenantId);
        if (rootTenant == null)
        {
            return Unauthorized(new { error = "invalid_user" });
        }

        var userId = User.FindFirstValue(BlocksContext.USER_ID_CLAIM) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "invalid_user" });
        }

        var user = await _authenticationRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return Unauthorized(new { error = "invalid_user" });
        }

        if (!CanImpersonateTargetTenant(rootTenantId, request.TargetTenantId, targetTenant))
        {
            await WriteImpersonationAuditEventAsync("impersonation_start_denied", userId, request.TargetTenantId, "WARN", "forbidden_target");
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "forbidden",
                error_description = "Root tenant cannot impersonate this target tenant"
            });
        }

        if (!Request.Cookies.TryGetValue($"{IdpConstants.AccessTokenCookieName}_{rootTenantId}", out var rootAccessToken)
            || !Request.Cookies.TryGetValue($"{IdpConstants.RefreshTokenCookieName}_{rootTenantId}", out var rootRefreshToken)
            || string.IsNullOrWhiteSpace(rootAccessToken)
            || string.IsNullOrWhiteSpace(rootRefreshToken))
        {
            return Unauthorized(new { error = "session_expired" });
        }

        var cacheClient = HttpContext.RequestServices.GetRequiredService<ICacheClient>();
        var rootRefreshCacheRaw = await cacheClient.GetStringValueAsync(rootRefreshToken);
        var rootRefreshCache = string.IsNullOrWhiteSpace(rootRefreshCacheRaw)
            ? null
            : JsonSerializer.Deserialize<RefreshTokenCache>(rootRefreshCacheRaw);

        if (rootRefreshCache == null || rootRefreshCache.ExpiresUtc <= DateTime.UtcNow)
        {
            return Unauthorized(new { error = "session_expired" });
        }

        var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
        if (authConfiguration == null)
        {
            return BadRequest(new { error = "auth_config_missing" });
        }

        BackupRootSession(rootAccessToken, rootRefreshToken, rootTenantId, rootTenant.CookieDomain, rootRefreshCache.ExpiresUtc);

        var originalContext = BlocksContext.GetContext();
        try
        {
            SetTenantContextForTokenIssuance(request.TargetTenantId, user);

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.Password,
                ClientId = request.ClientId ?? string.Empty,
                OrganizationId = string.IsNullOrWhiteSpace(request.OrgId) ? "default" : request.OrgId,
                IsImpersonation = true,
                OriginalTenantId = rootTenantId,
                TargetTenantId = request.TargetTenantId,
                ImpersonatorUserId = userId,
                Request = Request
            };

            var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, authConfiguration, user);
            if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                return BuildTokenResponse(tokenResponse);
            }

            var state = new ImpersonationState
            {
                RootTenantId = rootTenantId,
                TargetTenantId = request.TargetTenantId,
                OrgId = tokenRequest.OrganizationId,
                StartedAtUtc = DateTime.UtcNow
            };

            WriteImpersonationStateCookie(state, rootTenant.CookieDomain, tokenResponse.RefreshExpiresUtc);
            AppendCookies(tokenResponse);

            _logger.LogInformation("Impersonation started by user {UserId} from root tenant {RootTenantId} to target tenant {TargetTenantId}", userId, rootTenantId, request.TargetTenantId);
            await WriteImpersonationAuditEventAsync("impersonation_started", userId, request.TargetTenantId, "INFO", "success", rootTenantId);

            return Ok(new
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

    [HttpPost("stop-impersonation")]
    [Authorize]
    public async Task<IActionResult> StopImpersonation()
    {
        var cacheClient = HttpContext.RequestServices.GetRequiredService<ICacheClient>();
        string? targetTenantId = null;

        if (TryReadImpersonationState(out var state))
        {
            targetTenantId = state.TargetTenantId;
            var impRefreshCookieName = $"{IdpConstants.RefreshTokenCookieName}_{state.TargetTenantId}";
            if (Request.Cookies.TryGetValue(impRefreshCookieName, out var impRefreshToken) && !string.IsNullOrWhiteSpace(impRefreshToken))
            {
                await cacheClient.RemoveKeyAsync(impRefreshToken);
            }
        }

        var restored = await TryRestoreRootSessionAsync("manual_stop");
        if (!restored)
        {
            await WriteImpersonationAuditEventAsync("impersonation_stop_failed", User.FindFirstValue(BlocksContext.USER_ID_CLAIM), targetTenantId, "WARN", "session_expired");
            return Unauthorized(new { error = "session_expired" });
        }

        _logger.LogInformation("Impersonation stopped manually and root session restored");
        await WriteImpersonationAuditEventAsync("impersonation_stopped", User.FindFirstValue(BlocksContext.USER_ID_CLAIM), targetTenantId, "INFO", "success");
        return Ok(new
        {
            mode = "root",
            status = "restored",
            reason = "manual_stop"
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
        if (configuration == null)
        {
            return BadRequest(new { error = "auth_config_missing" });
        }

        var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
            ? _authenticationService.CookieToken(Request)
            : request.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return BadRequest(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is required" });
        }

        var cacheClient = HttpContext.RequestServices.GetRequiredService<ICacheClient>();
        var cachedRefreshToken = await cacheClient.GetStringValueAsync(refreshToken);
        if (string.IsNullOrWhiteSpace(cachedRefreshToken))
        {
            if (await TryRestoreRootSessionAsync("impersonation_expired"))
            {
                return Ok(new { mode = "root", status = "restored", reason = "impersonation_expired" });
            }

            if (TryReadImpersonationState(out _))
            {
                return Unauthorized(new { error = "session_expired" });
            }

            return BadRequest(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
        }

        var tokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(cachedRefreshToken);
        if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
        {
            if (await TryRestoreRootSessionAsync("impersonation_expired"))
            {
                return Ok(new { mode = "root", status = "restored", reason = "impersonation_expired" });
            }

            if (TryReadImpersonationState(out _))
            {
                return Unauthorized(new { error = "session_expired" });
            }

            return BadRequest(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
        }

        var user = await _authenticationRepository.GetUserByIdAsync(tokenCache.UserId);
        if (user == null)
        {
            return Unauthorized(new { error = "invalid_user" });
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
            Request = Request
        };

        var result = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);

        if (TryReadImpersonationState(out _))
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return BuildTokenResponse(result);
            }

            AppendCookies(result);
            return Ok(new
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

        return BuildTokenResponse(result);
    }

    [HttpGet("userinfo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUserInfo()
    {
        var tenantId = BlocksContext.GetContext()?.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Unauthorized(new { error = "tenant_not_resolved" });
        }

        var principal = await _authenticationService.GetPrincipalFromTokenAsync(Request, tenantId, false);
        if (principal == null)
        {
            return Unauthorized(new { error = "invalid_token" });
        }

        var claims = principal.Claims
            .GroupBy(claim => claim.Type)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1 ? (object)group.First().Value : group.Select(claim => claim.Value).ToArray());

        return Ok(claims);
    }

    [HttpGet("login-options")]
    [AllowAnonymous]
    public Task<IActionResult> GetLoginOptions()
    {
        return _authenticationService.GetLoginOptionsAsync();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        var result = await _authenticationService.LogoutUser(request.RefreshToken ?? string.Empty, Request);
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        _authenticationService.DeleteCookie(Request);
        return Ok(result);
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        var result = await _authenticationService.LogoutUser(string.Empty, Request);
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        _authenticationService.DeleteCookie(Request);
        return Ok(result);
    }

    private bool IsCurrentTenantRoot()
    {
        var tenantId = GetCurrentTenantId();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        return _tenants.GetTenantByID(tenantId)?.IsRootTenant ?? false;
    }

    private string? GetCurrentTenantId()
    {
        return User.FindFirstValue(BlocksContext.TENANT_ID_CLAIM)
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

    private async Task<bool> TryRestoreRootSessionAsync(string reason)
    {
        if (!TryReadImpersonationState(out var state))
        {
            return false;
        }

        if (!Request.Cookies.TryGetValue(RootAccessBackupCookieName, out var rootAccessToken)
            || !Request.Cookies.TryGetValue(RootRefreshBackupCookieName, out var rootRefreshToken)
            || !Request.Cookies.TryGetValue(RootTenantBackupCookieName, out var rootTenantId)
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

        var cacheClient = HttpContext.RequestServices.GetRequiredService<ICacheClient>();
        var refreshCacheRaw = await cacheClient.GetStringValueAsync(rootRefreshToken);
        var refreshCache = string.IsNullOrWhiteSpace(refreshCacheRaw)
            ? null
            : JsonSerializer.Deserialize<RefreshTokenCache>(refreshCacheRaw);

        if (refreshCache == null || refreshCache.ExpiresUtc <= DateTime.UtcNow)
        {
            return false;
        }

        var accessExpiry = GetJwtExpiryUtc(rootAccessToken) ?? DateTime.UtcNow.AddMinutes(15);
        Response.Cookies.Append($"{IdpConstants.AccessTokenCookieName}_{rootTenantId}", rootAccessToken, CreateCookieOptions(rootTenant.CookieDomain, accessExpiry));
        Response.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{rootTenantId}", rootRefreshToken, CreateCookieOptions(rootTenant.CookieDomain, refreshCache.ExpiresUtc));

        ClearImpersonationCookies(state, rootTenant.CookieDomain);
        _logger.LogInformation("Impersonation session restored to root tenant {RootTenantId} due to {Reason}", rootTenantId, reason);
        await WriteImpersonationAuditEventAsync("impersonation_restored", refreshCache.ImpersonatorUserId ?? refreshCache.UserId, state.TargetTenantId, "INFO", reason, rootTenantId);

        return true;
    }

    private async Task WriteImpersonationAuditEventAsync(string eventType, string? userId, string? targetTenantId, string severity, string status, string? rootTenantId = null)
    {
        try
        {
            var entry = new Blocks.Genesis.Auth.AuditLogModel
            {
                EventType = eventType,
                UserId = userId,
                TenantId = rootTenantId ?? BlocksContext.GetContext()?.TenantId,
                Severity = severity,
                Status = status,
                Timestamp = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                Details = string.IsNullOrWhiteSpace(targetTenantId) ? null : $"target_tenant={targetTenantId}"
            };

            await _auditLogRepo.CreateAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist impersonation audit event {EventType}", eventType);
        }
    }

    private void BackupRootSession(string rootAccessToken, string rootRefreshToken, string rootTenantId, string? cookieDomain, DateTime refreshExpiresUtc)
    {
        var accessExpiry = GetJwtExpiryUtc(rootAccessToken) ?? DateTime.UtcNow.AddMinutes(15);

        Response.Cookies.Append(RootAccessBackupCookieName, rootAccessToken, CreateCookieOptions(cookieDomain, accessExpiry));
        Response.Cookies.Append(RootRefreshBackupCookieName, rootRefreshToken, CreateCookieOptions(cookieDomain, refreshExpiresUtc));
        Response.Cookies.Append(RootTenantBackupCookieName, rootTenantId, CreateCookieOptions(cookieDomain, refreshExpiresUtc));
    }

    private void WriteImpersonationStateCookie(ImpersonationState state, string? cookieDomain, DateTime expiresUtc)
    {
        var json = JsonSerializer.Serialize(state);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        Response.Cookies.Append(ImpersonationStateCookieName, encoded, CreateCookieOptions(cookieDomain, expiresUtc));
    }

    private bool TryReadImpersonationState(out ImpersonationState state)
    {
        state = new ImpersonationState();

        if (!Request.Cookies.TryGetValue(ImpersonationStateCookieName, out var encoded) || string.IsNullOrWhiteSpace(encoded))
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

    private void ClearImpersonationCookies(ImpersonationState state, string? rootCookieDomain)
    {
        DeleteCookie(ImpersonationStateCookieName, rootCookieDomain);
        DeleteCookie(RootAccessBackupCookieName, rootCookieDomain);
        DeleteCookie(RootRefreshBackupCookieName, rootCookieDomain);
        DeleteCookie(RootTenantBackupCookieName, rootCookieDomain);
        DeleteCookie($"{IdpConstants.AccessTokenCookieName}_{state.TargetTenantId}", rootCookieDomain);
        DeleteCookie($"{IdpConstants.RefreshTokenCookieName}_{state.TargetTenantId}", rootCookieDomain);
    }

    private void DeleteCookie(string cookieName, string? domain)
    {
        Response.Cookies.Delete(cookieName, new CookieOptions
        {
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            Path = "/",
            Secure = true,
            SameSite = SameSiteMode.None
        });

        if (!string.IsNullOrWhiteSpace(domain))
        {
            Response.Cookies.Delete(cookieName, new CookieOptions
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

    private static void SetTenantContextForTokenIssuance(string tenantId, Iam.DomainService.Entities.User user)
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
                user.Email,
                Array.Empty<string>(),
                user.UserName,
                string.Empty,
                $"{user.FirstName} {user.LastName}".Trim(),
                string.Empty,
                tenantId,
                string.Empty
            });

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
                user.Email,
                Array.Empty<string>(),
                user.UserName,
                string.Empty,
                $"{user.FirstName} {user.LastName}".Trim(),
                string.Empty,
                tenantId
            });

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

    private IActionResult BuildTokenResponse(TokenResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            var statusCode = response.StatusCode > 0 ? response.StatusCode : StatusCodes.Status400BadRequest;
            return StatusCode(statusCode, new
            {
                error = response.Error,
                error_description = response.ErrorDescription,
                redirect_url = response.SsoUserRedirectUrl
            });
        }

        AppendCookies(response);
        return Ok(new
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

    private void AppendCookies(TokenResponse response)
    {
        var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
        var accessCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
        var refreshCookieOptions = CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

        if (!string.IsNullOrWhiteSpace(response.AccessToken))
        {
            Response.Cookies.Append($"{IdpConstants.AccessTokenCookieName}_{tenantId}", response.AccessToken, accessCookieOptions);
        }

        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            Response.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{tenantId}", response.RefreshToken, refreshCookieOptions);
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

public class EmbeddedLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
}

public class SocialLoginRequest
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
}

public class SwitchOrganizationRequest
{
    public string OrganizationId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}

public class ImpersonationRequest
{
    public string TargetTenantId { get; set; } = string.Empty;
    public string? OrgId { get; set; }
    public string? ClientId { get; set; }
}

public class ImpersonationState
{
    public string RootTenantId { get; set; } = string.Empty;
    public string TargetTenantId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
}

public class RefreshRequest
{
    public string? RefreshToken { get; set; }
    public string? ClientId { get; set; }
    public string? OrganizationId { get; set; }
}