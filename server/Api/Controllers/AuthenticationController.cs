using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IAccountService _accountService;
    private readonly IAuthenticationFlowService _authenticationFlowService;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        IAccountService accountService,
        IAuthenticationFlowService authenticationFlowService)
    {
        _authenticationService = authenticationService;
        _accountService = accountService;
        _authenticationFlowService = authenticationFlowService;
    }

    [HttpPost("recover")]
    public async Task<IActionResult> Recover([FromBody] RecoveryUserRequest command)
    {
        var result = await _accountService.RecoverAccountAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest command)
    {
        var result = await _accountService.ResetAccountPasswordAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest command)
    {
        var result = await _accountService.ChangePasswordAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] EmbeddedLoginRequest request)
    {
        var result = await _authenticationFlowService.ExecuteEmbeddedLoginAsync(request, Request);
        return await _authenticationService.BuildFlowResultAsync(result, HttpContext);
    }

    [HttpPost("login/social")]
    public async Task<IActionResult> SocialLogin([FromBody] SocialLoginRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSocialLoginAsync(request, Request);
        return await _authenticationService.BuildFlowResultAsync(result, HttpContext);
    }

    [HttpPost("switch-org")]
    [Authorize]
    public async Task<IActionResult> SwitchOrg([FromBody] SwitchOrganizationRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSwitchOrganizationAsync(request, User, Request);
        return await _authenticationService.BuildFlowResultAsync(result, HttpContext);
    }

    [HttpPost("impersonate")]
    [Authorize]
    public async Task<IActionResult> Impersonate([FromBody] ImpersonationRequest request)
    {
        return await _authenticationFlowService.ExecuteImpersonateAsync(request, User, Request, Response);
    }

    [HttpPost("impersonation/stop")]
    [Authorize]
    public async Task<IActionResult> StopImpersonation()
    {
        return await _authenticationFlowService.ExecuteStopImpersonationAsync(User, Request, Response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        return await _authenticationFlowService.ExecuteRefreshAsync(request, User, Request, Response);
    }

    [HttpGet("userinfo")]
    [Authorize]
    public IActionResult GetUserInfo()
    {
        var (isValid, userInfo) = _authenticationService.BuildOidcUserInfo(User);
        if (!isValid)
        {
            return Unauthorized(new { error = "invalid_token", error_description = "missing_sub_claim" });
        }

        return Ok(userInfo);
    }

    [HttpGet("login-options")]
    public Task<IActionResult> GetLoginOptions()
    {
        return _authenticationService.GetLoginOptionsAsync();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        var refreshToken = string.IsNullOrWhiteSpace(request.RefreshToken)
            ? _authenticationService.CookieToken(Request)
            : request.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = "refresh token is required for service logout."
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

    [HttpPost("internal/logout-idp")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public async Task<IActionResult> LogoutIdpInternal([FromBody] LogoutRequest request)
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

    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll([FromBody] LogoutAllRequest? request = null)
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
}