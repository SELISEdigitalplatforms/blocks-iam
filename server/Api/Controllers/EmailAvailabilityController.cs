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

    // Reject GET so the SPA fallback cannot 200 an email-bearing URL (ZAP 10024).
    [HttpGet("email/available")]
    [AllowAnonymous]
    public IActionResult IsEmailAvailableGetRejected()
        => StatusCode(405, new { error = "use POST" });

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
