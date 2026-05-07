using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Authentication.DomainService.OAuth.RequestModel;
using System.Security.Claims;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Token Lifecycle Management Service
    /// Handles OIDC token refresh and logout operations
    /// </summary>
    public interface ITokenLifecycleService
    {
        /// <summary>
        /// Exchange authorization code for tokens using OIDC Authorization Code Flow + PKCE
        /// Validates request and delegates to authorization flow logic
        /// </summary>
        Task<IActionResult> ExchangeOidcCodeAsync(OidcCodeExchangeRequest request, HttpRequest httpRequest, HttpResponse httpResponse);
    }
}
