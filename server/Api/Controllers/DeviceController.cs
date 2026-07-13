using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Browser-facing RFC 8628 device flow endpoints. The SPA already owns /oidc/* so it ships /device/*
/// too. This controller exposes the JSON payloads the SPA consumes; HTML responses fall through to
/// the SPA fallback configured in <c>Program.cs</c>.
/// </summary>
[ApiController]
[Route("device")]
public class DeviceController : ControllerBase
{
    private readonly DeviceVerificationController _verification;
    private readonly DeviceAuthorizationEndpoint _deviceAuthorization;

    public DeviceController(
        DeviceVerificationController verification,
        DeviceAuthorizationEndpoint deviceAuthorization)
    {
        _verification = verification;
        _deviceAuthorization = deviceAuthorization;
    }

    /// <summary>
    /// GET /device — entry. The SPA reads <c>user_code</c> from the query string and renders the
    /// form. For Accept: application/json callers we return a redirect hint.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Entry()
    {
        return _verification.EntryAsync(Request);
    }

    /// <summary>
    /// POST /device — accepts a user_code, mints an interactionId, and either returns the
    /// continue endpoint (already authenticated) or the OIDC login URL.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Submit([FromBody] DeviceInteractionRequest request, CancellationToken ct)
    {
        return await _verification.BeginAsync(request, HttpContext, ct);
    }

    /// <summary>
    /// GET /continue/{interactionId} — returns the consent payload for the SPA to render.
    /// </summary>
    [HttpGet("/continue/{interactionId}")]
    [AllowAnonymous]
    public async Task<IActionResult> Continue([FromRoute] string interactionId, CancellationToken ct)
    {
        return await _verification.ContinueAsync(interactionId, HttpContext, ct);
    }

    /// <summary>
    /// POST /approve — records allow/deny decision.
    /// </summary>
    [HttpPost("/approve")]
    [AllowAnonymous]
    public async Task<IActionResult> Approve([FromBody] DeviceApproveRequest request, CancellationToken ct)
    {
        return await _verification.ApproveAsync(request, HttpContext, ct);
    }
}