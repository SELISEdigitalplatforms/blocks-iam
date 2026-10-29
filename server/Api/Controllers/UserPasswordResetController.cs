using Blocks.Genesis;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// An administrator sends a user the password-reset mail. Protected by its own permission and
/// asks no captcha; the public forgot-password flow (auth/recover) keeps its captcha.
/// </summary>
[ApiController]
[Route("iam")]
public class UserPasswordResetController : ControllerBase
{
    public const string ResetUserPasswordPermission = "blocks-iam::iam::reset-user-password";

    private readonly IAdminPasswordResetService _adminPasswordResetService;

    public UserPasswordResetController(IAdminPasswordResetService adminPasswordResetService)
    {
        _adminPasswordResetService = adminPasswordResetService;
    }

    [HttpPost("users/reset-password")]
    [ProtectedEndPoint(ResetUserPasswordPermission)]
    public async Task<IActionResult> SendResetMail([FromBody] AdminPasswordResetRequest request)
    {
        var result = await _adminPasswordResetService.SendResetMailAsync(request);
        if (AccessGrantErrors.IsGrantNotAllowed(result.Errors))
        {
            return StatusCode(StatusCodes.Status403Forbidden, result);
        }

        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
