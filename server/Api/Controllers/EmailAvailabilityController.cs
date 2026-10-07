using Blocks.Genesis;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Email availability / existence checks. Kept separate from <see cref="IamController"/>
/// so addresses are handled on their own surface (POST bodies — ZAP 10024 / CWE-598).
/// </summary>
[ApiController]
[Route("iam")]
public class EmailAvailabilityController : ControllerBase
{
    private readonly IUserManagementQueryService _userManagementQueryService;

    public EmailAvailabilityController(IUserManagementQueryService userManagementQueryService)
    {
        _userManagementQueryService = userManagementQueryService;
    }

    // DEPRECATED, kept only so existing callers keep working across the rollout.
    // The address rides in the query string here, which is what ZAP 10024 / CWE-598
    // flags; POST is the supported form. Remove once every caller has moved --
    // blocks-os is the last known one (client/app/cross-modules/idp/iam/services).
    [Obsolete("Use POST iam/email/available. Scheduled for removal once callers have migrated.")]
    [HttpGet("email/available")]
    [AllowAnonymous]
    public async Task<IActionResult> IsEmailAvailableFromQuery([FromQuery] string? email)
    {
        // Bound as an optional string, so a missing address reaches the check below and gets the
        // same fixed 400 body as the POST form. Binding the request model made the framework
        // answer first with a ProblemDetails carrying a per-request traceId.
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "email is required" });
        }

        var result = await _userManagementQueryService.IsUserAvailableAsync(new IsEmailAvailableRequest { Email = email });
        return Ok(new IsEmailAvailableResponse
        {
            IsAvailable = result
        });
    }

    // POST body so the address is not reflected in the URL (CWE-598 / ZAP 10024).
    [HttpPost("email/available")]
    [AllowAnonymous]
    public async Task<IActionResult> IsEmailAvailable([FromBody] IsEmailAvailableRequest query)
    {
        if (query == null || string.IsNullOrWhiteSpace(query.Email))
        {
            return BadRequest(new { error = "email is required" });
        }

        var result = await _userManagementQueryService.IsUserAvailableAsync(query);
        return Ok(new IsEmailAvailableResponse
        {
            IsAvailable = result
        });
    }

    // DEPRECATED, kept only so existing callers keep working across the rollout.
    // Unlike email/available there was no GET stub here at all, so a stale caller hit
    // the SPA fallback and got 200 + index.html instead of a usable status code.
    [Obsolete("Use POST iam/users/exists. Scheduled for removal once callers have migrated.")]
    [HttpGet("users/exists")]
    [Authorize]
    public async Task<IActionResult> IsUserExistFromQuery([FromQuery] string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "email is required" });
        }

        var result = await _userManagementQueryService.IsUserExistAsync(email);
        return Ok(result);
    }

    // POST body so addresses are not placed in the URL (ZAP 10024 / CWE-598).
    [HttpPost("users/exists")]
    [Authorize]
    public async Task<IActionResult> IsUserExist([FromBody] IsEmailAvailableRequest? body)
    {
        var email = body?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "email is required" });
        }

        var result = await _userManagementQueryService.IsUserExistAsync(email);
        return Ok(result);
    }
}
