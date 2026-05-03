using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DomainService.Authentication
{
    public interface IAuthorizationFlowService
    {
        Task<IActionResult> AuthorizeAsync(
            string client_id,
            string response_type,
            string redirect_uri,
            string scope,
            string state,
            string nonce,
            string code_challenge,
            string code_challenge_method,
            string? prompt,
            string? tenant_id,
            ClaimsPrincipal userPrincipal,
            HttpRequest request,
            HttpResponse response);

        Task<IActionResult> SelectAccountAsync(string userId, string? tenantId, HttpRequest request, HttpResponse response);

        Task<IActionResult> TokenAsync(string grantType, HttpRequest request);
    }
}
