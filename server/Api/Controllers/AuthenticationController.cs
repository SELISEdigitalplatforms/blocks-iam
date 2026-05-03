using Blocks.Genesis;
using DomainService.Authentication;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using DomainService.Utilities;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
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
    [AllowAnonymous]
    public async Task<IActionResult> Recover([FromBody] RecoveryUserRequest command)
    {
        var result = await _accountService.RecoverAccountAsync(command);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
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
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] EmbeddedLoginRequest request)
    {
        var result = await _authenticationFlowService.ExecuteEmbeddedLoginAsync(request, Request);
        return BuildFlowResult(result);
    }

    [HttpPost("social-login")]
    [AllowAnonymous]
    public async Task<IActionResult> SocialLogin([FromBody] SocialLoginRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSocialLoginAsync(request, Request);
        return BuildFlowResult(result);
    }

    [HttpPost("switch-org")]
    [Authorize]
    public async Task<IActionResult> SwitchOrg([FromBody] SwitchOrganizationRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSwitchOrganizationAsync(request, User, Request);
        return BuildFlowResult(result);
    }

    [HttpPost("impersonate")]
    [Authorize]
    public async Task<IActionResult> Impersonate([FromBody] ImpersonationRequest request)
    {
        return await _authenticationFlowService.ExecuteImpersonateAsync(request, User, Request, Response);
    }

    [HttpPost("stop-impersonation")]
    [Authorize]
    public async Task<IActionResult> StopImpersonation()
    {
        return await _authenticationFlowService.ExecuteStopImpersonationAsync(User, Request, Response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        return await _authenticationFlowService.ExecuteRefreshAsync(request, User, Request, Response);
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
    [Authorize]
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
    [Authorize]
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

    private IActionResult BuildFlowResult(AuthenticationFlowResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error,
                error_description = result.ErrorDescription
            });
        }

        if (result.TokenResponse == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "server_error",
                error_description = "Authentication flow returned no response"
            });
        }

        return BuildTokenResponse(result.TokenResponse);
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