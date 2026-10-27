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
    /// <summary>
    /// The IdP session this token is minted under (OIDC <c>sid</c>). Left blank by flows that
    /// have no interactive session, and the claim is then omitted rather than emitted empty.
    /// </summary>
    public string? Sid { get; set; }
    public List<string> Amr { get; set; } = [];
    public List<string> Roles { get; set; } = [];
    public List<string> Resources { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public bool IsLinkAuthentication { get; set; }
    public List<string> RestrictedRoles { get; set; } = [];
    public List<string> RestrictedPermissions { get; set; } = [];

    /// <summary>
    /// The user's <c>TokenVersion</c>, emitted as <c>token_version</c> on access tokens only. Genesis
    /// needs it (with <see cref="SecurityStamp"/>) to attach a delegation grant to queued work. Null
    /// omits both claims.
    /// </summary>
    public int? TokenVersion { get; set; }

    /// <summary>The user's <c>SecurityStamp</c>, emitted as <c>security_stamp</c> beside <see cref="TokenVersion"/>.</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>
    /// Not a token claim: the address of the browser that signed in (the authorize request). The
    /// refresh-token record uses it instead of the address of whoever redeems the code, which for a
    /// confidential client is a server.
    /// </summary>
    public string? SignInIpAddress { get; set; }

    /// <summary>Not a token claim: the user agent of the browser that signed in. See <see cref="SignInIpAddress"/>.</summary>
    public string? SignInUserAgent { get; set; }
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
    public string? CreatedByUserAgent { get; set; }
    public string? IdpSessionId { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool Impersonated { get; set; } = false;

    public string? TargetedTenantId { get; set; } = string.Empty;

    public string? ImpersonatedUserId { get; set; } = string.Empty;

    /// <summary>True when the code was minted from a signup-link restricted session.</summary>
    public bool IsLinkAuthentication { get; set; }

    /// <summary>Frozen link roles to intersect with the user's current org roles at exchange/refresh.</summary>
    public List<string> RestrictedRoles { get; set; } = [];

    /// <summary>Frozen link permissions to intersect at exchange/refresh.</summary>
    public List<string> RestrictedPermissions { get; set; } = [];

    /// <summary>
    /// The store this code was read from, set by the read and never stored. The consume of the same
    /// request goes to the same store.
    /// </summary>
    [BsonIgnore]
    [JsonIgnore]
    public AuthorizationCodeSource Source { get; set; } = AuthorizationCodeSource.Redis;
}

public enum AuthorizationCodeSource
{
    Redis,
    Mongo
}

/// <summary>Result of the single-use consume of an authorization code.</summary>
public enum AuthorizationCodeConsumeResult
{
    /// <summary>First use: tokens may be minted.</summary>
    Consumed,
    /// <summary>Second use of a Redis code: the tokens made from it are revoked.</summary>
    Reused,
    /// <summary>Refused without more action (key gone, or a Mongo code used before).</summary>
    Refused
}

/// <summary>What a first exchange minted from a code; kept with the code so a reuse can revoke it.</summary>
public sealed class AuthorizationCodeMint
{
    [JsonPropertyName("lineageId")]
    public string LineageId { get; set; } = string.Empty;

    [JsonPropertyName("jti")]
    public string Jti { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("accessExpiresAt")]
    public DateTime AccessExpiresAt { get; set; }
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
