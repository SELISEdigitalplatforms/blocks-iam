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

    /// <summary>Returns the tenant's effective, default-filled OIDC UI template.</summary>
    [HttpGet]
    [ProtectedEndPoint("blocks-iam::iam::oidc-clients")]
    public async Task<IActionResult> GetOidcTemplate()
    {
        return Ok(await _authenticationDomainService.GetOidcTemplateForManagementAsync());
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
