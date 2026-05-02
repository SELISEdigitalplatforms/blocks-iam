using System.Security.Claims;
using Api.Controllers;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using DomainService.Authentication;
using DomainService.Dtos;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using DomainService.Utilities;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly IAccountService _accountService;
    private readonly ChangeControllerContext _changeControllerContext;
    private readonly PasswordAuthenticationService _passwordAuthenticationService;
    private readonly SocialAuthorizationService _socialAuthorizationService;
    private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
    private readonly ILogger<AuthenticationController> _logger;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        IConfiguration configuration,
        IAuthenticationDomainService domainService,
        IAuthenticationRepository authenticationRepository,
        IAccountService accountService,
        ChangeControllerContext changeControllerContext,
        IConfigurationService cloudConfigurationService,
        PasswordAuthenticationService passwordAuthenticationService,
        SocialAuthorizationService socialAuthorizationService,
        RefreshTokenAuthenticationService refreshTokenAuthenticationService,
        ILogger<AuthenticationController> logger)
    {
        _authenticationService = authenticationService;
        _authenticationRepository = authenticationRepository;
        _accountService = accountService;
        _changeControllerContext = changeControllerContext;
        _passwordAuthenticationService = passwordAuthenticationService;
        _socialAuthorizationService = socialAuthorizationService;
        _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
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

        var userId = User.FindFirstValue(BlocksContext.USER_ID_CLAIM) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "invalid_user" });
        }

        var user = await _authenticationRepository.GetUserByIdAsync(userId);
        if (user == null || !user.Memberships.Any(m => m.OrganizationId == request.OrganizationId))
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
            return BadRequest(new { error = OAuthError.InvalidRefreshToken, error_description = "Refresh token is invalid or expired" });
        }

        var tokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(cachedRefreshToken);
        if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
        {
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
            Request = Request
        };

        var result = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration, user);
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

public class RefreshRequest
{
    public string? RefreshToken { get; set; }
    public string? ClientId { get; set; }
    public string? OrganizationId { get; set; }
}