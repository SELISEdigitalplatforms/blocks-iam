using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Shared.RequestModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Authentication.DomainService.Authentication
{
    public interface IAuthenticationFlowService
    {
        Task<AuthenticationFlowResult> ExecuteEmbeddedLoginAsync(EmbeddedLoginRequest request, HttpRequest httpRequest);
        Task<AuthenticationFlowResult> ExecuteSocialLoginAsync(SocialLoginRequest request, HttpRequest httpRequest);
        Task<AuthenticationFlowResult> ExecuteSwitchOrganizationAsync(SwitchOrganizationRequest request, ClaimsPrincipal principal, HttpRequest httpRequest);
        Task<IActionResult> ExecuteRefreshAsync(RefreshRequest request, ClaimsPrincipal principal, HttpRequest httpRequest, HttpResponse httpResponse);
        Task<IActionResult> ExecuteImpersonateAsync(ImpersonateRequest request, HttpRequest httpRequest, HttpResponse httpResponse);
        Task<IActionResult> ExecuteStopImpersonationAsync(StopImpersonationRequest request, HttpRequest httpRequest, HttpResponse httpResponse);
    }

    public class AuthenticationFlowResult
    {
        public TokenResponse? TokenResponse { get; set; }
        public int StatusCode { get; set; } = StatusCodes.Status400BadRequest;
        public string? Error { get; set; }
        public string? ErrorDescription { get; set; }
    }

    public class GetContextForProjectResult
    {
        public bool IsSuccess { get; set; }
        public string? ContextId { get; set; }
        public string? Error { get; set; }
    }
}
