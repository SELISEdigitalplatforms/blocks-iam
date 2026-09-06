using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// IDP Service
    /// Handles identity provider flow initiation and callback handling for OIDC
    /// </summary>
    public interface IIdpService
    {
        /// <summary>
        /// Start authentication flow with identity provider
        /// Generates OIDC state, nonce, and PKCE parameters
        /// Returns redirect to provider authorize endpoint
        ///
        /// <para>
        /// With <paramref name="flow"/> set to <c>signup</c> the client is validated the
        /// same way, but the returned <c>redirect_uri</c> points at the IAM signup page
        /// rather than at authorize: no PKCE, and no cached flow context, because nothing
        /// on that path ever reaches <see cref="HandleCallbackAsync"/> to redeem one.
        /// </para>
        /// </summary>
        Task<IActionResult> StartAuthenticationFlowAsync(string clientId, string redirectUri, string? forwardedTo, string? flow = null, HttpRequest? httpRequest = null);

        /// <summary>
        /// Handle authorization code callback from identity provider
        /// Validates state, exchanges code for tokens, creates user session
        /// RFC 6749: OAuth 2.0 Authorization Code Flow | RFC 7636: PKCE
        /// </summary>
        Task<IActionResult> HandleCallbackAsync(string? code, string? state, string? error, string? error_description, HttpRequest httpRequest, HttpResponse httpResponse);

        Task<IActionResult> GetUiConfigAsync();
    }
}
