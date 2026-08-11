using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Authentication.DomainService.Authentication
{
    public interface IAuthenticationService
    {
        Task<IActionResult> BuildFlowResultAsync(AuthenticationFlowResult result, HttpContext httpContext);
        Task<bool> UpdateIdpSessionForLogoutAsync(HttpContext httpContext, ClaimsPrincipal user, bool isGlobalLogout);
        Task<bool> UpdateIdpSessionForLogoutAsync(HttpContext httpContext, ClaimsPrincipal user, bool isGlobalLogout, IEnumerable<string>? fallbackSessionIds);
        void ClearIdpSessionCookie(HttpResponse response);
        Task<LogoutResponse> LogoutUser(string refreshToken, HttpRequest httpRequest);
        Task<LogoutResponse> LogoutAll(HttpRequest httpRequest);
        Task<LogoutResponse> LogoutUserFromAllSites(HttpRequest httpRequest);
        Task<LogoutFlowResult> ExecuteLogoutAsync(LogoutRequest request, HttpContext httpContext);
        string CookieToken(HttpRequest request);
        bool DeleteCookie(HttpRequest request);
        bool DeleteAllCookies(HttpRequest request);
        Task AppendSessionCookies(HttpContext httpContext, string? accessToken, string? refreshToken, DateTime? accessExpiresUtc = null, DateTime? refreshExpiresUtc = null);
        Task<IActionResult> GetLoginOptionsAsync();
        Task<IActionResult> GetSocialAuthorizationUrlAsync(string clientId, string redirectUri);
        Task<IActionResult> GetOidcSocialAuthorizationUrlAsync(string providerClientId, string oidcState, string providerRedirectUri);
        Task<OidcClientRegistration> GetClientCredentialAsync(string clientId);
        Task<object> HandleTokenResponseConditionallyAsync(TokenResponse response, HttpResponse httpResponse, bool useTokensCookie, string? clientId = null);
        Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(HttpRequest request, string tenantId, bool IsUserInfoGetRequest = false);
        Task<(bool IsValid, Dictionary<string, object> UserInfo)> BuildOidcUserInfoAsync(ClaimsPrincipal principal);
        Task<bool> TriggerBackchannelLogoutAllAsync(HttpRequest httpRequest);
        Task<bool> EnsureIdpSessionForOidcCallbackAsync(HttpContext httpContext, string userId, string tenantId);
        Task<BaseResponse> CreateIdentityProviderAsync(SaveIdentityProviderRequest request);
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider);
        Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id);
        Task<List<IdentityProvider>> GetAllIdentityProvidersAsync();
        Task<BaseResponse> UpdateIdentityProviderAsync(string id, UpdateIdentityProviderRequest request);
        Task<BaseResponse> DeleteIdentityProviderAsync(string id);
        Task<BaseResponse> UpdateIdentityProviderStatusAsync(string id, bool isActive);
        Task<RotateOidcClientSecretResponse> RotateOidcClientSecretAsync(string itemId);
        Task<IActionResult> ExecuteImpersonateAsync(ImpersonateRequest request, HttpRequest httpRequest, HttpResponse httpResponse);
        Task<IActionResult> ExecuteStopImpersonationAsync(StopImpersonationRequest request, HttpRequest httpRequest, HttpResponse httpResponse);
    }

public sealed class LogoutFlowResult
    {
        public LogoutResponse? LogoutResponse { get; set; }
        public int StatusCode { get; set; } = StatusCodes.Status400BadRequest;
        public string? Error { get; set; }
        public string? ErrorDescription { get; set; }
    }
}
