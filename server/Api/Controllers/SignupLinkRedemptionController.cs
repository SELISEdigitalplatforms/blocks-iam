using Authentication.DomainService.SignupLinks;
using Blocks.Genesis;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("iam")]
public class SignupLinkRedemptionController : ControllerBase
{
    private readonly ISignupLinkContextService _contextService;
    private readonly ISignupLinkRedemptionOrchestrator _redemptionOrchestrator;

    public SignupLinkRedemptionController(
        ISignupLinkContextService contextService,
        ISignupLinkRedemptionOrchestrator redemptionOrchestrator)
    {
        _contextService = contextService;
        _redemptionOrchestrator = redemptionOrchestrator;
    }

    [HttpGet("signup-links/context")]
    [AllowAnonymous]
    public async Task<IActionResult> GetContext(
        [FromHeader(Name = "X-Signup-Link-Code")] string? code,
        [FromHeader(Name = "X-Blocks-Key")] string? blocksKey)
    {
        var tenantId = BlocksContext.GetContext()?.TenantId ?? blocksKey;
        var result = await _contextService.GetContextAsync(code, tenantId);
        return Ok(result);
    }

    [HttpPost("signup-links/redeem")]
    [AllowAnonymous]
    public async Task<IActionResult> Redeem(
        [FromBody] RedeemSignupLinkRequest? request,
        [FromHeader(Name = "X-Blocks-Key")] string? blocksKey)
    {
        var tenantId = BlocksContext.GetContext()?.TenantId ?? blocksKey;
        return await _redemptionOrchestrator.RedeemAsync(
            request?.Code,
            tenantId,
            Request,
            Response);
    }

    [HttpPost("signup-links/redeem/mfa")]
    [AllowAnonymous]
    public async Task<IActionResult> RedeemMfa(
        [FromBody] RedeemSignupLinkMfaRequest? request)
    {
        return await _redemptionOrchestrator.CompleteRedeemMfaAsync(
            request?.MfaId,
            request?.MfaCode,
            Request,
            Response);
    }
}
