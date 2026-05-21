using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
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
        IOidcCallbackHandler oidcCallbackHandler,
        IAuthorizationFlowService authorizationFlowService)
    {
        _authenticationService = authenticationService;
        _accountService = accountService;
        _authenticationFlowService = authenticationFlowService;
        _oidcCallbackHandler = oidcCallbackHandler;
    }

    /// <summary>
    /// Execute user registration (Sign Up)
    /// Creates new user account with provided credentials
    /// Issues access and refresh tokens on success
    /// </summary>
    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<IActionResult> ExecuteSignup([FromBody] SignupUserRequest request)
    {
        var result = await _accountService.SignupAccountAsync(request);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
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

    #region Account Activation

    /// <summary>
    /// Activate user account
    /// Validates activation code and marks account as active
    /// User can log in after successful activation
    /// </summary>
    /// <param name="command">Activation request with user email and verification code</param>
    /// <returns>Activation result with user details</returns>
    /// <response code="200">Account activated successfully</response>
    /// <response code="400">Invalid or expired activation code</response>
    [HttpPost("activate")]
    [AllowAnonymous]
    public async Task<IActionResult> Activate([FromBody] ActivateUserRequest command)
    {
        var result = await _accountService.ActivateAccountAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Resend account activation email
    /// Generates new activation code and sends to user's email
    /// Use if user did not receive initial activation email
    /// </summary>
    /// <param name="command">Request with user email to resend activation</param>
    /// <returns>Activation code send result</returns>
    /// <response code="200">Activation email resent successfully</response>
    /// <response code="400">User not found or already activated</response>
    [HttpPost("resend-activation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendActivation([FromBody] ResendActivationRequest command)
    {
        var result = await _accountService.ResendActivationAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Validate account activation code
    /// Checks if activation code is valid without activating account
    /// Use to verify code before user interaction
    /// </summary>
    /// <param name="command">Request with email and activation code</param>
    /// <returns>Validation result indicating code validity</returns>
    /// <response code="200">Activation code is valid</response>
    /// <response code="400">Invalid or expired activation code</response>
    [HttpPost("validate-activation")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateActivationCode([FromBody] ValidateActivationCodeRequest command)
    {
        var result = await _accountService.ValidateAccountActivationCodeAsync(command);
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
    public Task<IActionResult> InitiateSocialAuthentication([FromQuery] string provider, [FromQuery] string redirectUri)
    {
        return _authenticationService.GetSocialAuthorizationUrlAsync(provider, redirectUri);
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
            provider ??= request.Provider;
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
    /// OIDC callback handler (Browser Redirect Pattern - GET)
    /// Receives authorization code from provider via browser redirect
    /// Exchanges code for tokens, validates JWT signature and claims
    /// RFC 6749: OAuth 2.0 | RFC 3986: OpenID Connect | RFC 7519: JWT | RFC 5280: X.509
    /// </summary>
    [HttpGet("oidc/callback")]
    [HttpPost("oidc/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleOidcCallbackGet(
        [FromQuery] string code,
        [FromQuery] string state,
        [FromQuery] string provider,
        [FromBody] OidcCallbackRequest? request = null)
    {
        if (request != null)
        {
            code = request.Code;
            state = request.State;
            provider = request.Provider;
        }

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
        DomainResolver.ResetToOriginalBlocksContextForImpersonation();
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

        // Keep a single logout contract for both embedded and OIDC flows.
        // For embedded flows this is effectively a no-op (no idp_session_id cookie).
        // For OIDC flows this removes current account from IDP session and clears cookie when session becomes empty.
        var shouldClearIdpSessionCookie = await _authenticationService.UpdateIdpSessionForLogoutAsync(HttpContext, User, isGlobalLogout: false);
        if (shouldClearIdpSessionCookie)
        {
            _authenticationService.ClearIdpSessionCookie(Response);
        }

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

    // <summary>
    /// Initiate user impersonation (Administrator Feature)
    /// Allows admins to impersonate users for support/debugging
    /// All actions audited and linked back to admin
    /// Requires admin role - cannot impersonate other admins
    /// </summary>
    [HttpPost("impersonate")]
    [Authorize]
    public async Task<IActionResult> InitiateImpersonation([FromBody] ImpersonateRequest request)
    {
        // Reset BlocksContext to original tenant context in case this impersonation request is coming from an existing impersonation session (organization switch or tenant switch within impersonation), we want to validate permissions and issue tokens based on the original/root tenant context and not the current impersonated context
        DomainResolver.ResetToOriginalBlocksContextForImpersonation();
        return await _authenticationFlowService.ExecuteImpersonateAsync(request, Request, Response);
    }

    /// <summary>
    /// Stop user impersonation (Revert to Original Admin)
    /// Admin stops impersonating user and reverts to original context
    /// </summary>
    [HttpPost("impersonation/stop")]
    [Authorize]
    public async Task<IActionResult> StopImpersonation([FromBody] StopImpersonationRequest request)
    {
        // Reset BlocksContext to original tenant context in case this impersonation request is coming from an existing impersonation session (organization switch or tenant switch within impersonation), we want to validate permissions and issue tokens based on the original/root tenant context and not the current impersonated context
        if(!BlocksContext.GetContext().Impersonated)
        {
            return BadRequest(new StopImpersonationResponse
            {
                error = "Not_allowed"
            });
        }
        
        DomainResolver.ResetToOriginalBlocksContextForImpersonation();
        return await _authenticationFlowService.ExecuteStopImpersonationAsync(request, Request, Response);
    }

    [HttpPost("impersonation/status")]
    [Authorize]
    public async Task<IActionResult> GetImpersonationStatus()
    {
        var blocksContext = BlocksContext.GetContext();

        return Ok(new
        {
            Impersonated = blocksContext.Impersonated,
            OriginalTenantId = blocksContext.OriginalTenantId,
            ImpersonatedTenantId = blocksContext.TenantId
        });
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
    public async Task<IActionResult> UpdateIdentityProviderStatus([FromRoute] string id, [FromBody] Authentication.DomainService.Shared.RequestModel.UpdateStatusRequest request)
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
            // Ensure IdP session is established before redirect for SSO continuity
            if (!string.IsNullOrWhiteSpace(result.BlocksUserId) && !string.IsNullOrWhiteSpace(result.TenantId))
            {
                await _authenticationService.EnsureIdpSessionForOidcCallbackAsync(HttpContext, result.BlocksUserId, result.TenantId);
            }

            // Frontend will receive code and exchange for token
            var separator = result.RedirectUri.Contains('?') ? "&" : "?";
            var redirectUrl = $"{result.RedirectUri}{separator}code={Uri.EscapeDataString(result.AuthorizationCode)}&state={Uri.EscapeDataString(result.OriginalState ?? string.Empty)}";
            return Redirect(redirectUrl);
        }
        else
        {
            // EMBEDDED FLOW: Use the same tenant-scoped cookie naming and security policy as login/refresh/logout.
            await _authenticationService.AppendSessionCookies(HttpContext, result.AccessToken, result.RefreshToken);

            // Redirect to home page or original URL if state contains it (optional enhancement)
            return Redirect("/");
        }
    }
}