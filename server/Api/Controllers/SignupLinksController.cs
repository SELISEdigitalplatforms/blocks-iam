using Blocks.Genesis;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("iam")]
public class SignupLinksController : ControllerBase
{
    private readonly ISignupLinkConfigurationService _service;

    public SignupLinksController(ISignupLinkConfigurationService service)
    {
        _service = service;
    }

    [HttpPost("signup-links/configurations")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> Create([FromBody] CreateSignupLinkConfigurationRequest request)
    {
        var result = await _service.CreateAsync(request);
        return result.IsSuccess ? Ok(result) : MapMutationError(result);
    }

    [HttpPatch("signup-links/configurations/{id}")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateSignupLinkConfigurationRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.IsSuccess ? Ok(result) : MapMutationError(result);
    }

    [HttpPost("signup-links/configurations/{id}/archive")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> Archive([FromRoute] string id)
    {
        var result = await _service.ArchiveAsync(id);
        return result.IsSuccess ? Ok(result) : MapMutationError(result);
    }

    [HttpGet("signup-links/configurations/{id}")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> GetById([FromRoute] string id)
    {
        var (response, notFound) = await _service.GetByIdAsync(id);
        if (notFound || response == null)
        {
            return NotFound(new { errors = new Dictionary<string, string> { { "ItemId", "Not found" } } });
        }

        return Ok(response);
    }

    [HttpPost("signup-links/configurations/query")]
    [ProtectedEndPoint("blocks-iam::iam::manage-signup-links")]
    public async Task<IActionResult> Query([FromBody] QuerySignupLinkConfigurationsRequest request)
    {
        var (response, errors) = await _service.QueryAsync(request ?? new QuerySignupLinkConfigurationsRequest());
        if (errors != null)
        {
            return BadRequest(new { errors });
        }

        return Ok(response);
    }

    private IActionResult MapMutationError(BaseMutationResponse result)
    {
        if (result.Errors != null
            && result.Errors.TryGetValue("ItemId", out var message)
            && string.Equals(message, "Not found", StringComparison.Ordinal))
        {
            return NotFound(new { errors = result.Errors });
        }

        return BadRequest(result);
    }
}
