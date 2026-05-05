using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("oidc-clients")]
[Authorize]
public class OidcClientsController : ControllerBase
{
    private readonly IAuthenticationDomainService _authenticationDomainService;

    public OidcClientsController(IAuthenticationDomainService authenticationDomainService)
    {
        _authenticationDomainService = authenticationDomainService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? projectKey)
    {
        var response = await _authenticationDomainService.GetOIDCClientsAsyncAsync();

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpGet("{clientId}")]
    public async Task<IActionResult> GetByClientId([FromRoute] string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new { error = "client_id_required", message = "clientId is required." });
        }

        var response = await _authenticationDomainService.GetOIDCClientAsyncAsync(clientId);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        if (response.oIDCClientCredential == null)
        {
            return NotFound(new { error = "oidc_client_not_found", message = $"OIDC client '{clientId}' not found." });
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] SaveOIDCClientRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "invalid_payload", message = "Request body is required." });
        }

        var response = await _authenticationDomainService.SaveOIDCClientAsync(request);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpDelete("{clientId}")]
    public async Task<IActionResult> Delete([FromRoute] string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new { error = "client_id_required", message = "clientId is required." });
        }

        var request = new DeleteOIDCClientRequest { ItemId = clientId };
        var response = await _authenticationDomainService.DeleteOIDCClientAsyncAsync(request);
        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
