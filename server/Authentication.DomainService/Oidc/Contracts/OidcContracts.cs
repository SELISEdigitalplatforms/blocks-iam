using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace Idp.DomainService.Oidc.Contracts;

public class OidcClaims
{
    public string Sub { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? OrgId { get; set; }
    public string? Nonce { get; set; }
    public long AuthTime { get; set; }
    public long Iat { get; set; }
    public string? ClientId { get; set; }
    public string? Audience { get; set; }
    public string? Scope { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? UserName { get; set; }
    public List<string> Roles { get; set; } = [];
    public List<string> Resources { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
}

public class AuthorizationCodeModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Code { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string RedirectUri { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string? Nonce { get; set; }
    public string? State { get; set; }
    public string CodeChallenge { get; set; } = string.Empty;
    public string CodeChallengeMethod { get; set; } = "S256";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIpAddress { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? UsedByIpAddress { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class RefreshTokenModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string TokenId { get; set; } = Guid.NewGuid().ToString("n");
    public string UserId { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? OrgId { get; set; }
    public string? ClientId { get; set; }
    public string? Audience { get; set; }
    public string? Scope { get; set; }
    public string? SessionId { get; set; }
    public DateTime SlidingExpiry { get; set; }
    public DateTime AbsoluteExpiry { get; set; }
    public bool IsRevoked { get; set; }
    public string? RevokeReason { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Impersonated { get; set; }

    public bool IsExpired()
    {
        var now = DateTime.UtcNow;
        return now >= SlidingExpiry || now >= AbsoluteExpiry;
    }
}

public class IdpSessionAccount
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTime LoginAt { get; set; }
}

public class IdpSessionModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string SessionId { get; set; } = Guid.NewGuid().ToString("n");
    public string TenantId { get; set; } = string.Empty;
    public List<IdpSessionAccount> Accounts { get; set; } = [];
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime IdleExpiry { get; set; }
    public DateTime AbsoluteExpiry { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsExpired()
    {
        var now = DateTime.UtcNow;
        return now >= IdleExpiry || now >= AbsoluteExpiry;
    }
}

public class AuditLogModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string EventType { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? ClientId { get; set; }
    public string? TenantId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string Severity { get; set; } = "INFO";
    public string? Status { get; set; }
    public string? Details { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ConsentGrantModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string UserId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public List<string> GrantedScopes { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DiscoveryMetadata
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("userinfo_endpoint")]
    public string UserInfoEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("revocation_endpoint")]
    public string RevocationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("introspection_endpoint")]
    public string IntrospectionEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("jwks_uri")]
    public string JwksUri { get; set; } = string.Empty;

    [JsonPropertyName("response_types_supported")]
    public IEnumerable<string> ResponseTypesSupported { get; set; } = ["code"];

    [JsonPropertyName("grant_types_supported")]
    public IEnumerable<string> GrantTypesSupported { get; set; } = ["authorization_code", "refresh_token", "client_credentials"];

    [JsonPropertyName("subject_types_supported")]
    public IEnumerable<string> SubjectTypesSupported { get; set; } = ["public"];

    [JsonPropertyName("id_token_signing_alg_values_supported")]
    public IEnumerable<string> IdTokenSigningAlgValuesSupported { get; set; } = [SecurityAlgorithms.RsaSha256];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IEnumerable<string> TokenEndpointAuthMethodsSupported { get; set; } = ["client_secret_basic", "private_key_jwt"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public IEnumerable<string> CodeChallengeMethodsSupported { get; set; } = ["S256"];

    [JsonPropertyName("scopes_supported")]
    public IEnumerable<string> ScopesSupported { get; set; } = ["openid", "profile", "email", "offline_access"];
}

public class OAuthAuthorizationServerMetadata
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("jwks_uri")]
    public string JwksUri { get; set; } = string.Empty;

    [JsonPropertyName("revocation_endpoint")]
    public string RevocationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("introspection_endpoint")]
    public string IntrospectionEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("response_types_supported")]
    public IEnumerable<string> ResponseTypesSupported { get; set; } = ["code"];

    [JsonPropertyName("grant_types_supported")]
    public IEnumerable<string> GrantTypesSupported { get; set; } = ["authorization_code", "refresh_token", "client_credentials"];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IEnumerable<string> TokenEndpointAuthMethodsSupported { get; set; } = ["client_secret_basic", "private_key_jwt"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public IEnumerable<string> CodeChallengeMethodsSupported { get; set; } = ["S256"];
}

public class JwksResponse
{
    [JsonPropertyName("keys")]
    public List<JwkKey> Keys { get; set; } = [];
}

public class JwkKey
{
    [JsonPropertyName("kty")]
    public string Kty { get; set; } = "RSA";

    [JsonPropertyName("use")]
    public string Use { get; set; } = "sig";

    [JsonPropertyName("kid")]
    public string Kid { get; set; } = string.Empty;

    [JsonPropertyName("alg")]
    public string Alg { get; set; } = SecurityAlgorithms.RsaSha256;

    [JsonPropertyName("n")]
    public string N { get; set; } = string.Empty;

    [JsonPropertyName("e")]
    public string E { get; set; } = string.Empty;
}