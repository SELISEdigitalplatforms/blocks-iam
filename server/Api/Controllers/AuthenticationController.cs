using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthenticationController : ControllerBase
{
    private const string IdpSessionCookieName = "idp_session_id";

    private readonly IAuthenticationService _authenticationService;
    private readonly IAccountService _accountService;
    private readonly IAuthenticationFlowService _authenticationFlowService;
    private readonly IIdpSessionService _idpSessionService;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        IAccountService accountService,
        IAuthenticationFlowService authenticationFlowService,
        IIdpSessionService idpSessionService)
    {
        _authenticationService = authenticationService;
        _accountService = accountService;
        _authenticationFlowService = authenticationFlowService;
        _idpSessionService = idpSessionService;
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
        return await BuildFlowResult(result);
    }

    [HttpPost("social-login")]
    public async Task<IActionResult> SocialLogin([FromBody] SocialLoginRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSocialLoginAsync(request, Request);
        return await BuildFlowResult(result);
    }

    [HttpPost("switch-org")]
    [Authorize]
    public async Task<IActionResult> SwitchOrg([FromBody] SwitchOrganizationRequest request)
    {
        var result = await _authenticationFlowService.ExecuteSwitchOrganizationAsync(request, User, Request);
        return await BuildFlowResult(result);
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
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        return await _authenticationFlowService.ExecuteRefreshAsync(request, User, Request, Response);
    }

    [HttpGet("userinfo")]
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

        var shouldClearIdpSessionCookie = await UpdateIdpSessionForLogoutAsync(isGlobalLogout: false);
        _authenticationService.DeleteCookie(Request);
        if (shouldClearIdpSessionCookie)
        {
            ClearIdpSessionCookie();
        }
        return Ok(result);
    }

    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        // IDP logout is SSO-session only: do not revoke service refresh tokens.
        var shouldClearIdpSessionCookie = await UpdateIdpSessionForLogoutAsync(isGlobalLogout: true);
        if (shouldClearIdpSessionCookie)
        {
            ClearIdpSessionCookie();
        }

        return Ok(new
        {
            IsSuccess = true,
            Message = "IdP session logged out. Service tokens remain valid until expiry."
        });
    }

    private async Task<IActionResult> BuildTokenResponse(TokenResponse response)
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
        await EnsureIdpSessionForLoginAsync(response);
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

    private async Task<IActionResult> BuildFlowResult(AuthenticationFlowResult result)
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

        return await BuildTokenResponse(result.TokenResponse);
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

    private async Task EnsureIdpSessionForLoginAsync(TokenResponse tokenResponse)
    {
        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            return;
        }

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenResponse.AccessToken);
        var userId = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.USER_ID_CLAIM)?.Value;
        var tenantId = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.TENANT_ID_CLAIM)?.Value;

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var sessionId = Request.Cookies[IdpSessionCookieName];
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
        else
        {
            var existingSession = await _idpSessionService.GetSessionAsync(sessionId);
            if (existingSession == null || existingSession.RevokedAt.HasValue || existingSession.IsExpired())
            {
                sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }
            else
            {
                var accountExists = existingSession.Accounts.Any(a =>
                    string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

                if (!accountExists)
                {
                    await _idpSessionService.AddAccountAsync(sessionId, userId, tenantId, userId);
                }
                else
                {
                    await _idpSessionService.UpdateActivityAsync(sessionId);
                }
            }
        }

        Response.Cookies.Append(IdpSessionCookieName, sessionId, CreateCookieOptions(tokenResponse.CookieDomain, tokenResponse.RefreshExpiresUtc));
    }

    private async Task<bool> UpdateIdpSessionForLogoutAsync(bool isGlobalLogout)
    {
        var sessionId = Request.Cookies[IdpSessionCookieName];
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return true;
        }

        if (isGlobalLogout)
        {
            await _idpSessionService.RevokeSessionAsync(sessionId, "logout_all");
            return true;
        }

        var userId = User.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var tenantId = User.FindFirst(BlocksContext.TENANT_ID_CLAIM)?.Value
            ?? User.FindFirst("tenant_id")?.Value
            ?? BlocksContext.GetContext()?.TenantId;

        await _idpSessionService.RemoveAccountAsync(sessionId, userId, tenantId);

        var session = await _idpSessionService.GetSessionAsync(sessionId);
        if (session == null || session.RevokedAt.HasValue || session.IsExpired())
        {
            return true;
        }

        return session.Accounts.Count == 0;
    }

    private void ClearIdpSessionCookie()
    {
        Response.Cookies.Delete(IdpSessionCookieName);
    }
}