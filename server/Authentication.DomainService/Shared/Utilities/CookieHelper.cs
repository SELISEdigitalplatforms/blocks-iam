using Authentication.DomainService.OAuth.ResponseModel;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// Centralised cookie write/clear helpers for auth tokens.
    ///
    /// Replaces four near-identical copies of AppendCookies / DeleteCookie that
    /// previously lived on AuthorizationFlowService, IdpService, AuthenticationService,
    /// and AuthenticationFlowService.
    /// </summary>
    public static class CookieHelper
    {
        /// <summary>
        /// Appends access and refresh-token cookies for the resolved domain.
        /// Returns false if response carries an error, has no access token, or domain is empty.
        /// </summary>
        public static bool AppendCookies(TokenResponse response, HttpResponse httpResponse, string? domain)
        {
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(domain))
            {
                return false;
            }

            var accessCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = DomainResolver.CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            DeleteAccessAndRefreshTokenCookies(httpResponse, domain, accessCookieOptions, refreshCookieOptions);

            httpResponse.Cookies.Append(domain, response.AccessToken, accessCookieOptions);

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{domain}", response.RefreshToken, refreshCookieOptions);
            }

            return true;
        }

        /// <summary>
        /// Deletes both the access-token cookie and the refresh-token cookie for the given domain.
        /// </summary>
        public static void DeleteAccessAndRefreshTokenCookies(
            HttpResponse httpResponse,
            string domain,
            CookieOptions accessCookieOptions,
            CookieOptions refreshCookieOptions)
        {
            httpResponse.Cookies.Delete(domain, accessCookieOptions);
            httpResponse.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{domain}", refreshCookieOptions);
        }
    }
}
