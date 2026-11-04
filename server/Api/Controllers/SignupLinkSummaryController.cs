using Blocks.Genesis;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("iam")]
public class SignupLinkSummaryController : ControllerBase
{
    private readonly ISignupLinkSummaryService _summaryService;

    public SignupLinkSummaryController(ISignupLinkSummaryService summaryService)
    {
        _summaryService = summaryService;
    }

    [HttpPost("signup-links/summary")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> Summary([FromBody] SignupLinkSummaryRequest request)
    {
        var (response, errors) = await _summaryService.SummarizeAsync(
            request ?? new SignupLinkSummaryRequest());
        if (errors != null)
        {
            return BadRequest(new { errors });
        }

        return Ok(response);
    }
}
