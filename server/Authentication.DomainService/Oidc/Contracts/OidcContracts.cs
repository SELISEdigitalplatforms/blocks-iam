using System.Text.Json.Serialization;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Authentication.DomainService.OAuth;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson.Serialization.Attributes;

namespace Idp.DomainService.Oidc.Contracts;

public sealed class OidcClaims
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
    public List<string> Amr { get; set; } = [];
    public List<string> Roles { get; set; } = [];
    public List<string> Resources { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
}

public sealed class AuthorizationCodeModel
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
    public List<string> Amr { get; set; } = [];
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIpAddress { get; set; }
    public string? IdpSessionId { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool Impersonated { get; set; } = false;

    public string? TargetedTenantId { get; set; } = string.Empty;

    public string? ImpersonatedUserId { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
public sealed class RefreshTokenModel
{
    [BsonId]
    public string TokenId { get; set; } = Guid.NewGuid().ToString("n");
    public string UserId { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? OrganizationId { get; set; }
    public string? ClientId { get; set; }
    public string? Audience { get; set; }
    public string? Scope { get; set; }
    public string? SessionId { get; set; }

    /// <summary>
    /// Identifies one refresh-token lineage: the sequence of tokens created by a single
    /// authentication and linked by rotation. Set to the token's own <see cref="TokenId"/> when a new
    /// lineage is created, and copied unchanged by every rotation. Null on documents written before
    /// lineage tracking shipped; such a document is read as a lineage of one (see
    /// <see cref="EffectiveRefreshTokenSessionId"/>).
    /// </summary>
    public string? RefreshTokenSessionId { get; set; }

    /// <summary>
    /// The <see cref="TokenId"/> of the successor minted when this token was rotated away. Written in
    /// the same update that sets <c>RevokeReason = "superseded_by_rotation"</c>, and used to replay a
    /// rotation for a request that still carries the predecessor inside the grace window. Null on every
    /// document written before the grace period shipped, and on every revocation for another reason.
    /// </summary>
    public string? SupersededByTokenId { get; set; }

    public string? GrantType { get; set; }
    public DeviceInformation? DeviceInformation { get; set; }
    public DateTime IssuedUtc { get; set; }
    public DateTime SlidingExpiry { get; set; }
    public DateTime AbsoluteExpiry { get; set; }
    public bool IsRevoked { get; set; }
    public string? RevokeReason { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Impersonated { get; set; }
    public string? ImpersonationId { get; set; }

    /// <summary>
    /// The lineage this token belongs to, treating a pre-lineage document as a lineage of one.
    /// </summary>
    public string EffectiveRefreshTokenSessionId =>
        string.IsNullOrWhiteSpace(RefreshTokenSessionId) ? TokenId : RefreshTokenSessionId!;

    public bool IsExpired()
    {
        var now = DateTime.UtcNow;
        return now >= SlidingExpiry || now >= AbsoluteExpiry;
    }

    /// <summary>
    /// A token is active only while the server would actually accept it: unrevoked and past neither
    /// clock. This deliberately matches <see cref="IsExpired"/> so listings never advertise a session
    /// the refresh endpoint would reject.
    /// </summary>
    public bool IsActive(DateTime now) => !IsRevoked && now < SlidingExpiry && now < AbsoluteExpiry;
}

[BsonIgnoreExtraElements]
public sealed class IdpSessionAccount
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTime LoginAt { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class IdpSessionModel
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

public sealed class ConsentGrantModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string UserId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public List<string> GrantedScopes { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class DiscoveryMetadata
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

    [JsonPropertyName("device_authorization_endpoint")]
    public string DeviceAuthorizationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("jwks_uri")]
    public string JwksUri { get; set; } = string.Empty;

    [JsonPropertyName("response_types_supported")]
    public IEnumerable<string> ResponseTypesSupported { get; set; } = ["code"];

    [JsonPropertyName("grant_types_supported")]
    public IEnumerable<string> GrantTypesSupported { get; set; } = [GrantTypes.AuthCode, GrantTypes.RefreshToken, GrantTypes.ClientCredential, GrantTypes.DeviceCode];

    [JsonPropertyName("subject_types_supported")]
    public IEnumerable<string> SubjectTypesSupported { get; set; } = ["public"];

    [JsonPropertyName("id_token_signing_alg_values_supported")]
    public IEnumerable<string> IdTokenSigningAlgValuesSupported { get; set; } = [SecurityAlgorithms.RsaSha256];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IEnumerable<string> TokenEndpointAuthMethodsSupported { get; set; } = ["client_secret_basic", "private_key_jwt", "none"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public IEnumerable<string> CodeChallengeMethodsSupported { get; set; } = [IdpConstants.PkceMethodS256];

    [JsonPropertyName("scopes_supported")]
    public IEnumerable<string> ScopesSupported { get; set; } = ["openid", "profile", "email", "offline_access"];
}

public sealed class OAuthAuthorizationServerMetadata
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

    [JsonPropertyName("device_authorization_endpoint")]
    public string DeviceAuthorizationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("response_types_supported")]
    public IEnumerable<string> ResponseTypesSupported { get; set; } = ["code"];

    [JsonPropertyName("grant_types_supported")]
    public IEnumerable<string> GrantTypesSupported { get; set; } = [GrantTypes.AuthCode, GrantTypes.RefreshToken, GrantTypes.ClientCredential, GrantTypes.DeviceCode];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IEnumerable<string> TokenEndpointAuthMethodsSupported { get; set; } = ["client_secret_basic", "private_key_jwt", "none"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public IEnumerable<string> CodeChallengeMethodsSupported { get; set; } = [IdpConstants.PkceMethodS256];
}

public sealed class JwksResponse
{
    [JsonPropertyName("keys")]
    public List<JwkKey> Keys { get; set; } = [];
}

public sealed class JwkKey
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
