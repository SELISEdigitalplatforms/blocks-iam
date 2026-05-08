using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// OIDC Client Management Controller
/// Manages OIDC client registration, configuration, and lifecycle
/// Requires authentication and admin privileges
/// </summary>
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

    /// <summary>
    /// Retrieve all OIDC clients for the tenant
    /// Returns list of registered OAuth 2.0 / OIDC client applications
    /// Sensitive fields (client_secret) are excluded from response
    /// </summary>
    /// <returns>List of OIDC clients with configuration metadata</returns>
    /// <response code="200">Successfully retrieved clients list</response>
    /// <response code="400">Invalid request parameters</response>
    /// <response code="401">Authentication required</response>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var response = await _authenticationDomainService.GetOidcClientsAsync();

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieve specific OIDC client by client ID
    /// Returns detailed configuration for single client application
    /// Sensitive fields (client_secret) are excluded from response
    /// </summary>
    /// <param name="clientId">Unique identifier of OIDC client</param>
    /// <returns>OIDC client configuration with metadata</returns>
    /// <response code="200">Successfully retrieved client details</response>
    /// <response code="400">client_id is required</response>
    /// <response code="401">Authentication required</response>
    /// <response code="404">Client not found</response>
    [HttpGet("{clientId}")]
    public async Task<IActionResult> GetByClientId([FromRoute] string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new { error = "client_id_required", message = "clientId is required." });
        }

        var response = await _authenticationDomainService.GetOidcClientAsync(clientId);

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

    /// <summary>
    /// Create or update OIDC client configuration (Upsert)
    /// Registers new OIDC client or updates existing configuration
    /// Validates client metadata and OAuth/OIDC compliance
    /// Returns generated or existing client_secret
    /// </summary>
    /// <param name="request">OIDC client configuration request</param>
    /// <returns>Saved OIDC client with credentials</returns>
    /// <response code="200">Successfully created/updated client</response>
    /// <response code="400">Invalid request payload or validation failure</response>
    /// <response code="401">Authentication required</response>
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

    /// <summary>
    /// Delete OIDC client configuration
    /// Removes client application and revokes all issued tokens
    /// Note: Deletion is irreversible
    /// </summary>
    /// <param name="clientId">Unique identifier of OIDC client to delete</param>
    /// <returns>Deletion confirmation result</returns>
    /// <response code="200">Successfully deleted client</response>
    /// <response code="400">client_id is required or validation failure</response>
    /// <response code="401">Authentication required</response>
    /// <response code="404">Client not found</response>
    [HttpDelete("{clientId}")]
    public async Task<IActionResult> Delete([FromRoute] string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new { error = "client_id_required", message = "clientId is required." });
        }

        var request = new DeleteOIDCClientRequest { ItemId = clientId };
        var response = await _authenticationDomainService.DeleteOidcClientAsync(request);
        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
