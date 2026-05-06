using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Shared.RequestModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Authentication.DomainService.Authentication
{
    public interface IAuthenticationService
    {
        Task<IActionResult> BuildFlowResultAsync(AuthenticationFlowResult result, HttpContext httpContext);
        Task<bool> UpdateIdpSessionForLogoutAsync(HttpContext httpContext, ClaimsPrincipal user, bool isGlobalLogout);
        void ClearIdpSessionCookie(HttpResponse response);
        Task<LogoutResponse> LogoutUser(string refreshToken, HttpRequest httpRequest);
        string CookieToken(HttpRequest request);
        bool DeleteCookie(HttpRequest request);
        Task<IActionResult> GetLoginOptionsAsync();
        Task<OidcClientRegistration> GetClientCredentialAsync(string clientId);
        Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(HttpRequest request, string tenantId, bool IsUserInfoGetRequest = false);
        Task<string> ConstructRedirectUriAsync(string clientId, AcknowledgeRequest request);
        (bool IsValid, Dictionary<string, object> UserInfo) BuildOidcUserInfo(ClaimsPrincipal principal);
        Task<bool> TriggerBackchannelLogoutAllAsync(HttpRequest httpRequest);
    }
}
