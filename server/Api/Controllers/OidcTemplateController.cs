using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Protected tenant-level OIDC UI template management API.</summary>
[ApiController]
[Route("oidc-template")]
public sealed class OidcTemplateController : ControllerBase
{
    private readonly IAuthenticationDomainService _authenticationDomainService;

    public OidcTemplateController(IAuthenticationDomainService authenticationDomainService)
    {
        _authenticationDomainService = authenticationDomainService;
    }

    /// <summary>Returns the tenant's stored OIDC UI template.</summary>
    [HttpGet]
    [ProtectedEndPoint("blocks-iam::iam::oidc-clients")]
    public async Task<IActionResult> GetOidcTemplate()
    {
        var template = await _authenticationDomainService.GetOidcTemplateForManagementAsync();
        // JsonResult is intentional: Ok(null) is formatted as 204 No Content by
        // ASP.NET Core, while this endpoint's contract should explicitly return
        // HTTP 200 with a JSON null when the tenant has not stored a template yet.
        return template is null
            ? new JsonResult(null) { StatusCode = StatusCodes.Status200OK }
            : Ok(template);
    }

    /// <summary>Validates and completely replaces the tenant's OIDC UI template.</summary>
    [HttpPut]
    [ProtectedEndPoint("blocks-iam::iam::mutate-oidc-clients")]
    public async Task<IActionResult> SaveOidcTemplate([FromBody] SaveOidcUiTemplateRequest request)
    {
        if (request is null)
        {
            return BadRequest(new SaveOidcUiTemplateResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Request", "Request body is required." }
                }
            });
        }

        var response = await _authenticationDomainService.SaveOidcUiTemplateRequestAsync(request);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }
}
