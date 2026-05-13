using Authentication.DomainService.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// IDP Controller
/// Handles identity provider authentication flow and token exchange operations
/// </summary>
[ApiController]
[Route("idp")]
public class IdpController : ControllerBase
{
    private readonly IIdpService _idpService;

    /// <summary>
    /// Initialize IDP controller with IDP service
    /// </summary>
    public IdpController(IIdpService idpService)
    {
        _idpService = idpService;
    }

    /// <summary>
    /// Initiate identity provider authentication flow
    /// Delegates to IDP service for OIDC param generation and URL building
    /// </summary>
    [HttpGet("initiate")]
    [AllowAnonymous]
    public async Task<IActionResult> InitiateAuthenticationFlow([FromQuery] string clientId)
    {
        return await _idpService.StartAuthenticationFlowAsync(clientId);
    }

    /// <summary>
    /// Handle authorization code callback from IdP
    /// Receives authorization code and state, exchanges for tokens, creates session
    /// RFC 6749: OAuth 2.0 Authorization Code Flow | RFC 7636: PKCE
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, [FromQuery] string? error_description)
    {
        return await _idpService.HandleCallbackAsync(code, state, error, error_description, Request, Response);
    }
}
