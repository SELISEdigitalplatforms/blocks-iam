using Authentication.DomainService.Authentication;
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
    private readonly DeviceVerificationService _verification;

    public DeviceController(
        DeviceVerificationService verification)
    {
        _verification = verification;
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
    /// POST /api/device/verify — validates a user_code and returns either the inline consent
    /// payload (<c>status: "ready"</c>) or an OIDC login URL (<c>status: "login_required"</c>).
    /// </summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify([FromBody] DeviceVerifyRequest request, CancellationToken ct)
    {
        return await _verification.VerifyAsync(request, HttpContext, ct);
    }

    /// <summary>
    /// POST /api/device/decision — records an allow/deny decision for the device authorization
    /// request identified by <c>user_code</c>. Returns the SPA's success-page redirect.
    /// </summary>
    [HttpPost("decision")]
    [AllowAnonymous]
    public async Task<IActionResult> Decision([FromBody] DeviceDecisionRequest request, CancellationToken ct)
    {
        return await _verification.DecisionAsync(request, HttpContext, ct);
    }
}
