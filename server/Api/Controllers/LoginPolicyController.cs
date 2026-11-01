using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Accounts;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>The tenant's sign-in policy: where (embedded, OIDC) and how (password, social) users may sign in.</summary>
    [ApiController]
    [Route("iam/login-policy")]
    public class LoginPolicyController : ControllerBase
    {
        private readonly ILoginPolicySettingsService _loginPolicySettings;

        public LoginPolicyController(ILoginPolicySettingsService loginPolicySettings)
        {
            _loginPolicySettings = loginPolicySettings;
        }

        [HttpGet]
        [ProtectedEndPoint("blocks-iam::iam::mutate-tenant-configs")]
        public Task<GetLoginPolicyResponse> Get() => _loginPolicySettings.GetAsync();

        [HttpPost]
        [ProtectedEndPoint("blocks-iam::iam::mutate-tenant-configs")]
        public async Task<IActionResult> Save([FromBody] TenantLoginPolicy request)
        {
            var result = await _loginPolicySettings.SaveAsync(request);
            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return AccessGrantErrors.IsGrantNotAllowed(result.Errors)
                ? StatusCode(StatusCodes.Status403Forbidden, result)
                : BadRequest(result);
        }
    }
}
