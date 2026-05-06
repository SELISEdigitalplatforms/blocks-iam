using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Authentication and Authorization Controller
/// 
/// Manages all authentication flows:
/// - Password-based authentication (embedded credentials)
/// - Social provider authentication (OAuth 2.0)
/// - OIDC federated identity (OpenID Connect 1.0)
/// 
/// All endpoints follow RESTful conventions with professional naming and comprehensive documentation.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IAccountService _accountService;
    private readonly IAuthenticationFlowService _authenticationFlowService;
    private readonly IOidcCallbackHandler _oidcCallbackHandler;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        IAccountService accountService,
        IAuthenticationFlowService authenticationFlowService,
        IOidcCallbackHandler oidcCallbackHandler)
    {
        _authenticationService = authenticationService;
        _accountService = accountService;
        _authenticationFlowService = authenticationFlowService;
        _oidcCallbackHandler = oidcCallbackHandler;
    }

    #region Password Authentication

    /// <summary>
    /// Retrieve available login options (identity providers and their metadata)
    /// No authentication required - public discovery endpoint
    /// </summary>
    [HttpGet("login-options")]
    [AllowAnonymous]
    public Task<IActionResult> RetrieveLoginOptions()
    {
        return _authenticationService.GetLoginOptionsAsync();
    }

    /// <summary>
    /// Execute password-based authentication (Embedded Login)
    /// Validates username and password against stored user account
    /// Issues access and refresh tokens on success
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> ExecutePasswordLogin([FromBody] EmbeddedLoginRequest request)
    {
        var result = await _authenticationFlowService.ExecuteEmbeddedLoginAsync(request, Request);
        return await _authenticationService.BuildFlowResultAsync(result, HttpContext);
    }

    /// <summary>
    /// Initiate account recovery (password reset flow)
    /// Sends recovery link to registered email address
    /// </summary>
    [HttpPost("recover")]
    [AllowAnonymous]
    public async Task<IActionResult> InitiateAccountRecovery([FromBody] RecoveryUserRequest request)
    {
        var result = await _accountService.RecoverAccountAsync(request);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Execute password reset with recovery token
    /// Validates token before allowing password change
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ExecutePasswordReset([FromBody] ResetPasswordRequest request)
    {
        var result = await _accountService.ResetAccountPasswordAsync(request);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Update password for authenticated user
    /// Requires current password for security validation
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> UpdatePassword([FromBody] ChangePasswordRequest request)
    {
        var result = await _accountService.ChangePasswordAsync(request);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    #endregion

    #region Social Provider Authentication (OAuth 2.0)

    /// <summary>
    /// Initiate social provider authentication (OAuth 2.0 Authorization Code Flow)
    /// Generates PKCE code challenge and state parameter
    /// Returns authorization URL to redirect user to social provider
    /// RFC 6749: OAuth 2.0 Framework | RFC 7636: PKCE
    /// </summary>
    [HttpGet("social/authorize")]
    [AllowAnonymous]
    public Task<IActionResult> InitiateSocialAuthentication([FromQuery] string provider)
    {
        return _authenticationService.GetSocialAuthorizationUrlAsync(provider);
    }

    /// <summary>
    /// Social provider callback handler (Both Browser Redirect & API Pattern)
    /// Receives authorization code from social provider via GET query params or POST body
    /// Exchanges code for tokens, validates JWT, creates/updates user
    /// Sets secure HTTP-only cookie with tokens
    /// Supports both patterns:
    /// - GET /social/callback?code=...&state=...&provider=... (Browser redirect)
    /// - POST /social/callback with request body (SPA/API pattern)
    /// RFC 6749: OAuth 2.0 | RFC 3986: OpenID Connect | RFC 7519: JWT
    /// </summary>
    [HttpGet("social/callback")]
    [HttpPost("social/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleSocialCallback(
        [FromQuery] string? code = null,
        [FromQuery] string? state = null,
        [FromQuery] string? provider = null,
        [FromBody] SocialLoginRequest? request = null)
    {
        // Handle POST body pattern
        if (request != null)
        {
            code = request.Code;
            state = request.State;
        }

        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { error = "authorization_code_missing", error_description = "Authorization code is required" });

        if (string.IsNullOrWhiteSpace(state))
            return BadRequest(new { error = "state_missing", error_description = "State parameter is required" });

        if (string.IsNullOrWhiteSpace(provider))
            return BadRequest(new { error = "provider_missing", error_description = "Provider name is required" });

        var loginRequest = new SocialLoginRequest { Code = code, State = state };
        var result = await _authenticationFlowService.ExecuteSocialLoginAsync(loginRequest, Request);
        return await _authenticationService.BuildFlowResultAsync(result, HttpContext);
    }

    #endregion

    #region OIDC Federated Authentication (OpenID Connect 1.0)

    /// <summary>
    /// Initiate OIDC authentication (Internal OIDC Clients)
    /// For services that use Blocks IDP as authentication provider
    /// RFC 6749: OAuth 2.0 | RFC 3986: OpenID Connect
    /// </summary>
    [HttpGet("oidc/authorize")]
    [AllowAnonymous]
    public Task<IActionResult> InitiateOidcAuthentication()
    {
        return _authenticationService.GetOidcAuthorizationUrlAsync();
    }

    /// <summary>
    /// Retrieve OIDC login page with provider options
    /// Displays available social providers for federated authentication
    /// RFC 3986: OpenID Connect Discovery
    /// </summary>
    [HttpGet("oidc/login-page")]
    [AllowAnonymous]
    public Task<IActionResult> RetrieveOidcLoginPage(
        [FromQuery] string clientId,
        [FromQuery] string state,
        [FromQuery] string redirectUri)
    {
        return _authenticationService.GetOidcLoginPageAsync(clientId, state, redirectUri);
    }

    /// <summary>
    /// Initiate OIDC social provider authentication
    /// Called when user selects provider on OIDC login page
    /// Links social authentication to OIDC client context
    /// </summary>
    [HttpGet("oidc/social/authorize")]
    [AllowAnonymous]
    public Task<IActionResult> InitiateOidcSocialAuthentication(
        [FromQuery] string provider,
        [FromQuery] string oidcState)
    {
        return _authenticationService.GetOidcSocialAuthorizationUrlAsync(provider, oidcState);
    }

    /// <summary>
    /// OIDC callback handler (API Pattern - POST)
    /// Receives authorization code from provider via POST request
    /// Exchanges code for tokens, validates JWT signature and claims
    /// RFC 6749: OAuth 2.0 | RFC 3986: OpenID Connect | RFC 7519: JWT | RFC 5280: X.509
    /// </summary>
    [HttpPost("oidc/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleOidcCallbackPost([FromBody] OidcCallbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Code))
            return BadRequest(new { error = "authorization_code_missing", error_description = "Authorization code is required" });

        if (string.IsNullOrWhiteSpace(request.State))
            return BadRequest(new { error = "state_missing", error_description = "State parameter is required" });

        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest(new { error = "provider_missing", error_description = "Provider name is required" });

        return await ProcessOidcCallback(request.Code, request.State, request.Provider);
    }

    /// <summary>
    /// OIDC callback handler (Browser Redirect Pattern - GET)
    /// Receives authorization code from provider via browser redirect
    /// Exchanges code for tokens, validates JWT signature and claims
    /// RFC 6749: OAuth 2.0 | RFC 3986: OpenID Connect | RFC 7519: JWT | RFC 5280: X.509
    /// </summary>
    [HttpGet("oidc/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleOidcCallbackGet(
        [FromQuery] string code,
        [FromQuery] string state,
        [FromQuery] string provider)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { error = "authorization_code_missing", error_description = "Authorization code is required" });

        if (string.IsNullOrWhiteSpace(state))
            return BadRequest(new { error = "state_missing", error_description = "State parameter is required" });

        if (string.IsNullOrWhiteSpace(provider))
            return BadRequest(new { error = "provider_missing", error_description = "Provider name is required" });

        return await ProcessOidcCallback(code, state, provider);
    }

    #endregion

    #region Token & Session Management

    /// <summary>
    /// Refresh access token using refresh token
    /// Validates refresh token and issues new tokens
    /// Maintains session continuity without re-authentication
    /// RFC 6749: OAuth 2.0 Refresh Token Grant
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshAccessToken([FromBody] RefreshRequest request)
    {
        return await _authenticationFlowService.ExecuteRefreshAsync(request, User, Request, Response);
    }

    /// <summary>
    /// Execute user logout
    /// Revokes refresh token, invalidates session, clears cookies
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> ExecuteLogout([FromBody] LogoutRequest request)
    {
        var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
            ? _authenticationService.CookieToken(Request)
            : request.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = "Refresh token is required for logout"
            });
        }

        var logoutResult = await _authenticationService.LogoutUser(refreshToken, Request);
        if (!logoutResult.IsSuccess)
        {
            return BadRequest(logoutResult);
        }

        _authenticationService.DeleteCookie(Request);
        return Ok(logoutResult);
    }

    /// <summary>
    /// Switch organization context (Multi-tenant Organization Switching)
    /// Authenticated user switches to different organization
    /// Reissues tokens with new organization context
    /// </summary>
    [HttpPost("switch-org")]
    [Authorize]
    public async Task<IActionResult> SwitchOrganizationContext([FromBody] SwitchOrganizationRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSwitchOrganizationAsync(request, User, Request);
        return await _authenticationService.BuildFlowResultAsync(result, HttpContext);
    }

    /// <summary>
    /// Initiate user impersonation (Administrator Feature)
    /// Allows admins to impersonate users for support/debugging
    /// All actions audited and linked back to admin
    /// Requires admin role - cannot impersonate other admins
    /// </summary>
    [HttpPost("impersonate")]
    [Authorize]
    public async Task<IActionResult> InitiateImpersonation([FromBody] ImpersonationRequest request)
    {
        return await _authenticationFlowService.ExecuteImpersonateAsync(request, User, Request, Response);
    }

    /// <summary>
    /// Stop user impersonation (Revert to Original Admin)
    /// Admin stops impersonating user and reverts to original context
    /// </summary>
    [HttpPost("impersonation/stop")]
    [Authorize]
    public async Task<IActionResult> StopImpersonation()
    {
        return await _authenticationFlowService.ExecuteStopImpersonationAsync(User, Request, Response);
    }

    /// <summary>
    /// Execute OIDC IDP logout (Internal Use)
    /// Special logout endpoint for OIDC identity provider sessions
    /// Clears IDP session state and revokes tokens
    /// Internal endpoint - not exposed in Swagger documentation
    /// </summary>
    [HttpPost("internal/logout-idp")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public async Task<IActionResult> ExecuteIdpLogout([FromBody] LogoutRequest request)
    {
        var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
            ? _authenticationService.CookieToken(Request)
            : request.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = "refresh token is required for idp logout."
            });
        }

        var logoutResult = await _authenticationService.LogoutUser(refreshToken, Request);
        if (!logoutResult.IsSuccess)
        {
            return BadRequest(logoutResult);
        }

        var shouldClearIdpSessionCookie = await _authenticationService.UpdateIdpSessionForLogoutAsync(HttpContext, User, isGlobalLogout: false);
        _authenticationService.DeleteCookie(Request);
        if (shouldClearIdpSessionCookie)
        {
            _authenticationService.ClearIdpSessionCookie(Response);
        }

        return Ok(logoutResult);
    }

    /// <summary>
    /// Execute global logout across all sessions (Logout All Devices)
    /// Revokes all refresh tokens for user across all devices
    /// User must re-authenticate on all devices
    /// Optionally triggers backchannel logout notifications
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> ExecuteGlobalLogout([FromBody] LogoutAllRequest? request = null)
    {
        request ??= new LogoutAllRequest();
        var logoutAllResult = await _authenticationService.LogoutUser(string.Empty, Request);
        if (!logoutAllResult.IsSuccess)
        {
            return BadRequest(new
            {
                IsSuccess = false,
                Message = "Service logout-all failed"
            });
        }

        _authenticationService.DeleteCookie(Request);

        var shouldClearIdpSessionCookie = await _authenticationService.UpdateIdpSessionForLogoutAsync(HttpContext, User, isGlobalLogout: true);
        if (shouldClearIdpSessionCookie)
        {
            _authenticationService.ClearIdpSessionCookie(Response);
        }

        var backchannelSuccess = true;
        if (request.UseBackchannel)
        {
            backchannelSuccess = await _authenticationService.TriggerBackchannelLogoutAllAsync(Request);
        }

        return Ok(new
        {
            IsSuccess = true,
            ServiceLoggedOut = true,
            SessionLoggedOut = true,
            BackchannelTriggered = request.UseBackchannel,
            BackchannelSuccess = backchannelSuccess
        });
    }

    #endregion

    #region User Information & Discovery

    /// <summary>
    /// Retrieve authenticated user information (OIDC UserInfo Endpoint)
    /// Returns user claims per OpenID Connect 1.0 specification
    /// Includes standard OIDC claims (sub, email, name, picture) and custom Blocks claims
    /// RFC 3986: OpenID Connect UserInfo Endpoint
    /// </summary>
    [HttpGet("userinfo")]
    [Authorize]
    public IActionResult RetrieveUserInformation()
    {
        var (isValid, userInfo) = _authenticationService.BuildOidcUserInfo(User);
        if (!isValid)
        {
            return Unauthorized(new { error = "invalid_token", error_description = "Missing required 'sub' claim in token" });
        }

        return Ok(userInfo);
    }

    #endregion

    #region Identity Provider Management (CRUD)

    /// <summary>
    /// Create identity provider configuration
    /// Registers new OAuth 2.0 / OIDC provider for tenant
    /// Validates configuration and tests JWKS endpoint
    /// Requires authorization (admin role)
    /// </summary>
    [HttpPost("identity-providers")]
    [Authorize]
    public async Task<IActionResult> CreateIdentityProvider([FromBody] IdentityProvider provider)
    {
        var result = await _authenticationService.CreateIdentityProviderAsync(provider);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet("identity-providers")]
    [Authorize]
    public async Task<IActionResult> GetAllIdentityProviders()
    {
        var providers = await _authenticationService.GetAllIdentityProvidersAsync();
        return Ok(new { data = providers, isSuccess = true });
    }

    /// <summary>
    /// Get identity provider by ID
    /// Retrieves specific provider configuration
    /// Does NOT return sensitive credentials (client_secret)
    /// </summary>
    [HttpGet("identity-providers/{id}")]
    [Authorize]
    public async Task<IActionResult> GetIdentityProviderById([FromRoute] string id)
    {
        var provider = await _authenticationService.GetIdentityProviderByIdAsync(id);
        if (provider == null)
            return NotFound(new { isSuccess = false, message = "Provider not found" });
        
        return Ok(new { data = provider, isSuccess = true });
    }

    /// <summary>
    /// Update identity provider configuration
    /// Modifies existing provider settings
    /// Validates configuration and tests endpoints if changed
    /// </summary>
    [HttpPut("identity-providers/{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateIdentityProvider([FromRoute] string id, [FromBody] IdentityProvider provider)
    {
        provider.ItemId = id;
        var result = await _authenticationService.UpdateIdentityProviderAsync(provider);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Enable or disable identity provider
    /// Toggles provider activation status without deleting configuration
    /// Preferred over deletion for temporary disabling
    /// </summary>
    [HttpPatch("identity-providers/{id}/status")]
    [Authorize]
    public async Task<IActionResult> UpdateIdentityProviderStatus([FromRoute] string id, [FromBody] UpdateStatusRequest request)
    {
        var result = await _authenticationService.UpdateIdentityProviderStatusAsync(id, request.IsActive);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    #endregion

    private async Task<IActionResult> ProcessOidcCallback(string code, string state, string provider)
    {
        // Exchange code for token
        var result = await _oidcCallbackHandler.HandleCallbackAsync(code, state, provider);

        if (!result.IsSuccess)
        {
            return BadRequest(new 
            { 
                error = "token_exchange_failed",
                error_description = result.ErrorMessage 
            });
        }

        // Check if this is OIDC flow (issuing authorization code) or embedded flow (setting cookie)
        if (result.IsOidcFlow && !string.IsNullOrWhiteSpace(result.AuthorizationCode) && !string.IsNullOrWhiteSpace(result.RedirectUri))
        {
            // OIDC FLOW: Issue authorization code and redirect to original redirect_uri
            // Frontend will receive code and exchange for token
            var redirectUrl = $"{result.RedirectUri}?code={Uri.EscapeDataString(result.AuthorizationCode)}&state={Uri.EscapeDataString(result.OriginalState)}";
            return Redirect(redirectUrl);
        }
        else
        {
            // EMBEDDED FLOW: Set secure HTTP-only cookie with access token
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = !IsLocalDevelopment(),  // Secure only in production
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow.AddHours(1),
                Path = "/"
            };

            Response.Cookies.Append("oidc_token", result.AccessToken, cookieOptions);

            // Optional: Set refresh token in separate cookie if available
            if (!string.IsNullOrWhiteSpace(result.RefreshToken))
            {
                var refreshCookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    Secure = !IsLocalDevelopment(),
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTime.UtcNow.AddDays(7),
                    Path = "/"
                };
                Response.Cookies.Append("oidc_refresh_token", result.RefreshToken, refreshCookieOptions);
            }

            // Redirect to dashboard
            return Redirect("/dashboard");
        }
    }

    private bool IsLocalDevelopment()
    {
        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        var isLocalhost = HttpContext.Request.Host.Host == "localhost" || 
                         HttpContext.Request.Host.Host == "127.0.0.1";
        return isDevelopment && isLocalhost;
    }
}