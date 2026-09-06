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
    /// Initiate identity provider authentication flow for a specific client
    /// Delegates to IDP service for OIDC param generation and URL building
    ///
    /// <para>
    /// <paramref name="flow"/> selects what the returned <c>redirect_uri</c> points at.
    /// Omitted (or anything other than <c>signup</c>) keeps today's behaviour: an
    /// authorize URL that starts a full OIDC request. <c>signup</c> returns a link
    /// straight to the IAM signup page instead — the client is validated identically,
    /// but no authorize request is begun.
    /// </para>
    /// <para>
    /// Both flows cache the same OIDC flow context, so <paramref name="forwardedTo"/>
    /// survives either round trip: a user who signs up, follows the signup page's link
    /// back to the login page and signs in still lands on the page they asked for.
    /// </para>
    /// </summary>
    [HttpGet("initiate")]
    [AllowAnonymous]
    public async Task<IActionResult> InitiateAuthenticationFlow([FromQuery] string clientId, [FromQuery] string redirectUri, [FromQuery] string? forwardedTo, [FromQuery] string? flow)
    {
        return await _idpService.StartAuthenticationFlowAsync(clientId, redirectUri, forwardedTo, flow, Request);
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

    [HttpGet("oidc-ui-config")]
    [AllowAnonymous]
    public async Task<IActionResult> OidcUiConfig()
    {
        return await _idpService.GetUiConfigAsync();
    }
}
