using Authentication.DomainService.SignupLinks;
using Blocks.Genesis;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("iam")]
public class SignupLinksController : ControllerBase
{
    private readonly ISignupLinkGenerationService _generationService;
    private readonly ISignupLinkContextService _contextService;
    private readonly ISignupLinkRedemptionOrchestrator _redemptionOrchestrator;

    public SignupLinksController(
        ISignupLinkGenerationService generationService,
        ISignupLinkContextService contextService,
        ISignupLinkRedemptionOrchestrator redemptionOrchestrator)
    {
        _generationService = generationService;
        _contextService = contextService;
        _redemptionOrchestrator = redemptionOrchestrator;
    }

    [HttpGet("signup-links/context")]
    [AllowAnonymous]
    public async Task<IActionResult> GetContext(
        [FromHeader(Name = "X-Signup-Link-Code")] string? code)
    {
        var tenantId = BlocksContext.GetContext()?.TenantId
            ?? Request.Headers["X-Blocks-Key"].FirstOrDefault();
        var result = await _contextService.GetContextAsync(code, tenantId);
        return Ok(result);
    }

    [HttpPost("signup-links/redeem")]
    [AllowAnonymous]
    public async Task<IActionResult> Redeem([FromBody] RedeemSignupLinkRequest? request)
    {
        var tenantId = BlocksContext.GetContext()?.TenantId
            ?? Request.Headers["X-Blocks-Key"].FirstOrDefault();
        return await _redemptionOrchestrator.RedeemAsync(
            request?.Code,
            tenantId,
            Request,
            Response);
    }

    [HttpPost("signup-links")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> Generate([FromBody] GenerateSignupLinkRequest request)
    {
        var result = await _generationService.GenerateAsync(request ?? new GenerateSignupLinkRequest());
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new { errors = result.Errors });
        }

        return Ok(result.Data);
    }

    [HttpPost("signup-links/query")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> QueryLinks([FromBody] QuerySignupLinksRequest request)
    {
        var (response, errors) = await _generationService.QueryAsync(request ?? new QuerySignupLinksRequest());
        if (errors != null)
        {
            return BadRequest(new { errors });
        }

        return Ok(response);
    }

    [HttpPost("signup-links/{linkId}/revoke")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> Revoke([FromRoute] string linkId)
    {
        var result = await _generationService.RevokeAsync(linkId);
        if (!result.IsSuccess)
        {
            if (result.Errors != null
                && result.Errors.TryGetValue("ItemId", out var message)
                && string.Equals(message, "Not found", StringComparison.Ordinal))
            {
                return NotFound(new { errors = result.Errors });
            }

            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("signup-links/revoke-by-configuration")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> RevokeByConfiguration([FromBody] RevokeSignupLinksByConfigurationRequest request)
    {
        var result = await _generationService.RevokeByConfigurationAsync(
            request ?? new RevokeSignupLinksByConfigurationRequest());
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
