using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Thin orchestrator entry point for the OIDC endpoints. All heavy lifting is delegated to
    /// focused services:
    /// <list type="bullet">
    /// <item><see cref="OidcLoginOrchestrator"/>  – <see cref="ExecuteOidcLoginAsync"/></item>
    /// <item><see cref="OidcAuthorizationEndpoint"/> – <see cref="AuthorizeAsync"/></item>
    /// <item><see cref="OidcTokenEndpoint"/>      – <see cref="TokenAsync"/></item>
    /// </list>
    /// The constructor shrank from 17 dependencies to 4 (S107 compliance).
    /// </summary>
    public sealed class AuthorizationFlowService : IAuthorizationFlowService
    {
        private readonly OidcLoginOrchestrator _loginOrchestrator;
        private readonly OidcAuthorizationEndpoint _authorizationEndpoint;
        private readonly OidcTokenEndpoint _tokenEndpoint;

        public AuthorizationFlowService(
            OidcLoginOrchestrator loginOrchestrator,
            OidcAuthorizationEndpoint authorizationEndpoint,
            OidcTokenEndpoint tokenEndpoint)
        {
            _loginOrchestrator = loginOrchestrator;
            _authorizationEndpoint = authorizationEndpoint;
            _tokenEndpoint = tokenEndpoint;
        }

        public Task<IActionResult> ExecuteOidcLoginAsync(OidcLoginRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            return _loginOrchestrator.ExecuteAsync(request, httpRequest, httpResponse);
        }

        public Task<IActionResult> AuthorizeAsync(
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
            HttpRequest request,
            HttpResponse response,
            string? blocksUserId = null,
            bool returnRedirectResponse = true,
            bool mfaCompleted = false)
        {
            return _authorizationEndpoint.AuthorizeAsync(
                client_id,
                response_type,
                redirect_uri,
                scope,
                state,
                nonce,
                code_challenge,
                code_challenge_method,
                prompt,
                tenant_id,
                request,
                response,
                blocksUserId,
                returnRedirectResponse,
                mfaCompleted);
        }

        public Task<IActionResult> TokenAsync(string grantType, HttpRequest request)
        {
            return _tokenEndpoint.TokenAsync(grantType, request);
        }
    }
}
