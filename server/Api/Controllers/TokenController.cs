using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth.RequestModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Token Lifecycle Management Controller
/// Handles OIDC token exchange operations
/// Token refresh and logout are handled via /auth endpoints in AuthenticationController
/// </summary>
[ApiController]
[Route("token")]
public class TokenController : ControllerBase
{
    private readonly ITokenLifecycleService _tokenLifecycleService;

    public TokenController(ITokenLifecycleService tokenLifecycleService)
    {
        _tokenLifecycleService = tokenLifecycleService;
    }

    /// <summary>
    /// Exchange authorization code for tokens (OIDC Authorization Code Flow + PKCE)
    /// Validates code, PKCE verifier, and client registration with OIDC provider
    /// Tokens handled per client configuration: either secure HttpOnly cookies or response body
    /// RFC 6749: OAuth 2.0 Authorization Code Flow | RFC 7636: PKCE
    /// </summary>
    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> ExchangeOidcCode([FromBody] OidcCodeExchangeRequest request)
    {
        return await _tokenLifecycleService.ExchangeOidcCodeAsync(request, Request, Response);
    }
}
