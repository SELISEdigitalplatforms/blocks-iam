using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Authentication.DomainService.Authentication
{
    public class OidcLoginRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("username")]
        public string? Username { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("password")]
        public string? Password { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("redirect_uri")]
        public string? RedirectUri { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("state")]
        public string? State { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nonce")]
        public string? Nonce { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("code_challenge")]
        public string? CodeChallenge { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("code_challenge_method")]
        public string? CodeChallengeMethod { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tenant_id")]
        public string? TenantId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("provider_client_id")]
        public string? ProviderClientId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("provider_redirect_uri")]
        public string? ProviderRedirectUri { get; set; }
    }

    public class OidcLoginResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; } = "success"; // "success" | "account_selection_required"

        [System.Text.Json.Serialization.JsonPropertyName("accounts")]
        public List<OidcAccountInfo>? Accounts { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("redirect_url")]
        public string? RedirectUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    public class OidcAccountInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("tenant_id")]
        public string TenantId { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tenant_name")]
        public string? TenantName { get; set; }
    }

    public interface IAuthorizationFlowService
    {
        Task<IActionResult> ExecuteOidcLoginAsync(OidcLoginRequest request, HttpRequest httpRequest, HttpResponse httpResponse);

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
            HttpResponse response,
            bool returnRedirectResponse = true);


        Task<IActionResult> TokenAsync(string grantType, HttpRequest request);
    }
}
