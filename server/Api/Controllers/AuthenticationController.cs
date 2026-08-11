using Authentication.DomainService.Authentication;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Authentication.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Iam.DomainService.Utilities;
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
    private readonly IAuthenticationConfigurationService _configurationService;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly IAuthenticationDomainService _authenticationDomainService;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        IAccountService accountService,
        IAuthenticationFlowService authenticationFlowService,
        IAuthenticationConfigurationService configurationService, IAuthenticationRepository authenticationRepository,
        IAuthenticationDomainService authenticationDomainService
    )
    {
        _authenticationService = authenticationService;
        _accountService = accountService;
        _authenticationFlowService = authenticationFlowService;
        _configurationService= configurationService;
        _authenticationRepository = authenticationRepository;
        _authenticationDomainService = authenticationDomainService;
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
    /// Initiate account recovery (password reset flow).
    /// INVARIANT: This endpoint always returns <c>200 OK</c> with <c>IsSuccess = true</c>
    /// for any well-formed request, regardless of whether the account exists, is active,
    /// or has ever been registered. The actual reason (unknown email / inactive user /
    /// send failure) is audited server-side only and never exposed in the response.
    /// For unknown or inactive users, the service silently routes the request to an
    /// activation email instead of a reset email to prevent account enumeration.
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
    [ProtectedEndPoint("blocks-iam::auth::change-password")]
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
    [ProtectedEndPoint("blocks-iam::auth::resend-activation")]
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
    [HttpGet("social/initiate")]
    [AllowAnonymous]
    public Task<IActionResult> InitiateSocialAuthentication([FromQuery] string clientId, [FromQuery] string redirectUri)
    {
        return _authenticationService.GetSocialAuthorizationUrlAsync(clientId, redirectUri);
    }

    /// <summary>
    /// Social provider callback handler (API Pattern)
    /// Receives authorization code from social provider via POST body
    /// Exchanges code for tokens, validates JWT, creates/updates user
    /// Sets secure HTTP-only cookie with tokens
    /// Endpoint:
    /// - POST /social/callback with request body
    /// RFC 6749: OAuth 2.0 | RFC 3986: OpenID Connect | RFC 7519: JWT
    /// </summary>
    [HttpPost("social/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleSocialCallback([FromBody] SocialLoginRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSocialLoginAsync(request, Request);

        return await _authenticationService.BuildFlowResultAsync(result, HttpContext);
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
        var result = await _authenticationService.ExecuteLogoutAsync(request ?? new LogoutRequest(), HttpContext);

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return BadRequest(new
            {
                error = result.Error,
                error_description = result.ErrorDescription
            });
        }

        if (result.StatusCode != StatusCodes.Status200OK && result.LogoutResponse is not null)
        {
            return BadRequest(result.LogoutResponse);
        }

        return Ok(result.LogoutResponse);
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
        if (!BlocksContext.GetContext().Impersonated)
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
        var logoutAllResult = await _authenticationService.LogoutAll(Request);
        if (!logoutAllResult.IsSuccess)
        {
            return BadRequest(new
            {
                IsSuccess = false,
                Message = "Service logout-all failed"
            });
        }

        _authenticationService.DeleteCookie(Request);

        var shouldClearIdpSessionCookie = await _authenticationService.UpdateIdpSessionForLogoutAsync(
            HttpContext,
            User,
            isGlobalLogout: true,
            logoutAllResult.IdpSessionIds);
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
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> RetrieveUserInformation()
    {
        var (isValid, userInfo) = await _authenticationService.BuildOidcUserInfoAsync(User);
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
    [ProtectedEndPoint("blocks-iam::auth::mutate-identity-providers")]
    public async Task<IActionResult> CreateIdentityProvider([FromBody] SaveIdentityProviderRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "invalid_payload", message = "Request body is required." });
        }

        var result = await _authenticationService.CreateIdentityProviderAsync(request);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet("identity-providers")]
    [ProtectedEndPoint("blocks-iam::auth::identity-providers")]
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
    [ProtectedEndPoint("blocks-iam::auth::identity-providers")]
    public async Task<IActionResult> GetIdentityProviderById([FromRoute] string id)
    {
        var provider = await _authenticationService.GetIdentityProviderByIdAsync(id);
        if (provider == null)
            return NotFound(new { isSuccess = false, message = "Provider not found" });

        return Ok(new { data = provider, isSuccess = true });
    }

    /// <summary>
    /// Update identity provider configuration
    /// Modifies existing provider settings (partial merge; null fields are left unchanged).
    /// Provider, ProviderType, Protocol and ClientId are immutable and must echo the existing value if supplied.
    /// </summary>
    [HttpPut("identity-providers/{id}")]
    [ProtectedEndPoint("blocks-iam::auth::mutate-identity-providers")]
    public async Task<IActionResult> UpdateIdentityProvider([FromRoute] string id, [FromBody] UpdateIdentityProviderRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "invalid_payload", message = "Request body is required." });
        }

        var result = await _authenticationService.UpdateIdentityProviderAsync(id, request);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Delete identity provider configuration
    /// Removes the provider and cascades to delete the related OIDC client registration (if any).
    /// Deletion is irreversible.
    /// </summary>
    [HttpDelete("identity-providers/{id}")]
    [ProtectedEndPoint("blocks-iam::auth::mutate-identity-providers")]
    public async Task<IActionResult> DeleteIdentityProvider([FromRoute] string id)
    {
        var result = await _authenticationService.DeleteIdentityProviderAsync(id);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Enable or disable identity provider
    /// Toggles provider activation status without deleting configuration
    /// Preferred over deletion for temporary disabling
    /// </summary>
    [HttpPatch("identity-providers/{id}/status")]
    [ProtectedEndPoint("blocks-iam::auth::mutate-identity-providers")]
    public async Task<IActionResult> UpdateIdentityProviderStatus([FromRoute] string id, [FromBody] UpdateStatusRequest request)
    {
        var result = await _authenticationService.UpdateIdentityProviderStatusAsync(id, request.IsActive);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    #endregion

    [HttpGet("config")]
    [ProtectedEndPoint("blocks-iam::auth::identity-config")]
    public async Task<IActionResult> GetAuthenticationConfiguration()
    {
        return await _configurationService.GetAuthenticationConfigAsync();
    }

    [HttpPost("config")]
    [ProtectedEndPoint("blocks-iam::auth::mutate-identity-config")]
    public async Task<BaseResponse> UpdateAuthenticationConfiguration([FromBody] UpdateAuthenticationConfigurationRequest configuration)
    {
        return await _configurationService.UpdateAuthenticationConfigAsync(configuration);
    }

    [HttpPost("user-codes")]
    [ProtectedEndPoint("blocks-iam::auth::mutate-user-pats")]
    public async Task<BaseResponse> GenerateUserCode([FromBody] GenerateUserCodeRequest request)
    {
        return await _authenticationDomainService.GenerateUserCodeByClientAsync(request);
    }

    [HttpGet("user-codes")]
    [ProtectedEndPoint("blocks-iam::auth::user-pats")]
    public async Task<List<GetUserCodesByUserIdResponse>> GetUserCodes()
    {
        return await _authenticationRepository.GetUserCodesByUserIdAsync(BlocksContext.GetContext()?.UserId);
    }

    #region Client Credential Management

    [HttpPost("client-credentials")]
    [ProtectedEndPoint("blocks-iam::auth::mutate-client-credentials")]
    public async Task<BaseResponse> SaveClientCredential([FromBody] SaveClientCredentialRequest request)
    {
        return await _authenticationDomainService.SaveClientCredentialAsync(request);
    }

    [HttpDelete("client-credentials/{id}")]
    [ProtectedEndPoint("blocks-iam::auth::mutate-client-credentials")]
    public async Task<BaseResponse> DeleteClientCredential([FromRoute] string id)
    {
        return await _authenticationDomainService.DeleteClientCredentialAsync(new DeleteClientCredentialRequest
        {
            ItemId = id
        });
    }

    [HttpGet("client-credentials")]
    [ProtectedEndPoint("blocks-iam::auth::client-credentials")]
    public async Task<List<ClientCredential>> GetClientCredentials()
    {
        return await _authenticationRepository.GetClientCredentialsAsync();
    }

    #endregion

}
