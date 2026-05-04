using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Blocks.Genesis.Auth.Services;

public interface ITokenGenerationService
{
    Task<string> GenerateIdTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds);
    Task<string> GenerateAccessTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds);
    Task<Blocks.Genesis.Auth.RefreshTokenModel> GenerateRefreshTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer);
}

public interface IPkceService
{
    Task<bool> ValidateVerifierAsync(string codeChallenge, string codeVerifier, string? codeChallengeMethod);
}

public interface IDiscoveryService
{
    Task<Blocks.Genesis.Auth.DiscoveryMetadata> GetMetadataAsync();
    Task<Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata> GetAuthorizationServerMetadataAsync();
}

public interface IJwksService
{
    Task<Blocks.Genesis.Auth.JwksResponse> GetKeysAsync();
}

public sealed class OidcSigningKeyMaterial
{
    public OidcSigningKeyMaterial()
    {
        var rsa = RSA.Create(2048);
        Rsa = rsa;
        SecurityKey = new RsaSecurityKey(rsa)
        {
            KeyId = Guid.NewGuid().ToString("n")
        };
        SigningCredentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.RsaSha256);
    }

    public RSA Rsa { get; }
    public RsaSecurityKey SecurityKey { get; }
    public SigningCredentials SigningCredentials { get; }
}

public class TokenGenerationService : ITokenGenerationService
{
    private readonly OidcSigningKeyMaterial _keyMaterial;

    public TokenGenerationService(OidcSigningKeyMaterial keyMaterial)
    {
        _keyMaterial = keyMaterial;
    }

    public Task<string> GenerateIdTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds)
    {
        return Task.FromResult(GenerateToken(claims, issuer, expiresInSeconds, includeNonce: true));
    }

    public Task<string> GenerateAccessTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds)
    {
        return Task.FromResult(GenerateToken(claims, issuer, expiresInSeconds, includeNonce: false));
    }

    public Task<Blocks.Genesis.Auth.RefreshTokenModel> GenerateRefreshTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer)
    {
        var now = DateTime.UtcNow;
        return Task.FromResult(new Blocks.Genesis.Auth.RefreshTokenModel
        {
            TokenId = Guid.NewGuid().ToString("n"),
            FamilyId = Guid.NewGuid().ToString("n"),
            UserId = claims.Sub,
            TenantId = claims.TenantId,
            OrgId = claims.OrgId,
            Audience = claims.Audience,
            Scope = claims.Scope,
            SlidingExpiry = now.AddMinutes(30),
            AbsoluteExpiry = now.AddHours(8)
        });
    }

    private string GenerateToken(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds, bool includeNonce)
    {
        var now = DateTime.UtcNow;
        var jwtClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, claims.Sub),
            new("tenant_id", claims.TenantId),
            new(JwtRegisteredClaimNames.Iat, claims.Iat.ToString(), ClaimValueTypes.Integer64),
            new("auth_time", claims.AuthTime.ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
        };

        if (!string.IsNullOrWhiteSpace(claims.OrgId))
        {
            jwtClaims.Add(new Claim("org_id", claims.OrgId));
        }

        if (!string.IsNullOrWhiteSpace(claims.ClientId))
        {
            jwtClaims.Add(new Claim("client_id", claims.ClientId));
        }

        if (!string.IsNullOrWhiteSpace(claims.Audience))
        {
            jwtClaims.Add(new Claim("aud", claims.Audience));
        }
        else if (!string.IsNullOrWhiteSpace(claims.ClientId))
        {
            jwtClaims.Add(new Claim("aud", claims.ClientId));
        }

        if (includeNonce && !string.IsNullOrWhiteSpace(claims.Nonce))
        {
            jwtClaims.Add(new Claim(JwtRegisteredClaimNames.Nonce, claims.Nonce));
        }

        foreach (var role in claims.Roles)
        {
            jwtClaims.Add(new Claim("role", role));
            jwtClaims.Add(new Claim("roles", role));
        }

        foreach (var resource in claims.Resources)
        {
            jwtClaims.Add(new Claim("resource", resource));
            jwtClaims.Add(new Claim("resources", resource));
        }

        foreach (var permission in claims.Permissions)
        {
            jwtClaims.Add(new Claim("permission", permission));
            jwtClaims.Add(new Claim("permissions", permission));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: claims.Audience ?? claims.ClientId,
            claims: jwtClaims,
            notBefore: now,
            expires: now.AddSeconds(expiresInSeconds),
            signingCredentials: _keyMaterial.SigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class PkceService : IPkceService
{
    public Task<bool> ValidateVerifierAsync(string codeChallenge, string codeVerifier, string? codeChallengeMethod)
    {
        if (!string.Equals(codeChallengeMethod, "S256", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        var encoded = Base64UrlEncoder.Encode(hash);
        return Task.FromResult(string.Equals(encoded, codeChallenge, StringComparison.Ordinal));
    }
}

public class DiscoveryService : IDiscoveryService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public DiscoveryService(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    public Task<Blocks.Genesis.Auth.DiscoveryMetadata> GetMetadataAsync()
    {
        var endpoints = ResolveEndpoints();
        return Task.FromResult(new Blocks.Genesis.Auth.DiscoveryMetadata
        {
            Issuer = endpoints.Issuer,
            AuthorizationEndpoint = endpoints.AuthorizationEndpoint,
            TokenEndpoint = endpoints.TokenEndpoint,
            UserInfoEndpoint = endpoints.UserInfoEndpoint,
            JwksUri = endpoints.JwksUri
        });
    }

    public Task<Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata> GetAuthorizationServerMetadataAsync()
    {
        var endpoints = ResolveEndpoints();
        return Task.FromResult(new Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata
        {
            Issuer = endpoints.Issuer,
            AuthorizationEndpoint = endpoints.AuthorizationEndpoint,
            TokenEndpoint = endpoints.TokenEndpoint,
            JwksUri = endpoints.JwksUri
        });
    }

    private ResolvedOidcEndpoints ResolveEndpoints()
    {
        var issuer = GetIssuer();
        var apiPrefix = ApplicationConfigurations.NormalizeApiRoutePrefixValue(_configuration["ApiRouting:Prefix"]);
        var serviceSegment = ResolveRequiredServiceName(_configuration);

        return new ResolvedOidcEndpoints
        {
            Issuer = issuer,
            AuthorizationEndpoint = BuildUrl(issuer, apiPrefix, serviceSegment, "oidc", "authorize"),
            TokenEndpoint = BuildUrl(issuer, apiPrefix, serviceSegment, "oidc", "token"),
            UserInfoEndpoint = BuildUrl(issuer, apiPrefix, serviceSegment, "auth", "userinfo"),
            JwksUri = BuildUrl(issuer, ".well-known", "jwks.json")
        };
    }

    private static string ResolveRequiredServiceName(IConfiguration configuration)
    {
        var serviceName = Environment.GetEnvironmentVariable("ServiceName") ?? configuration["ServiceName"];
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new InvalidOperationException("Missing required ServiceName configuration.");
        }

        return serviceName;
    }

    private string GetIssuer()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request != null)
        {
            var pathBase = request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
            return $"{request.Scheme}://{request.Host.Value}{pathBase}";
        }

        var configuredBaseUrl = Environment.GetEnvironmentVariable("BLOCKS_API_BASE_URL")
            ?? _configuration["BLOCKS_API_BASE_URL"]
            ?? _configuration["FrontendRuntime:BLOCKS_API_BASE_URL"];

        if (Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var uri))
        {
            return configuredBaseUrl!.TrimEnd('/');
        }

        return "https://localhost:5000";
    }

    private static string BuildUrl(string issuer, params string[] segments)
    {
        var parts = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment.Trim('/'));

        var suffix = string.Join('/', parts);
        return string.IsNullOrWhiteSpace(suffix) ? issuer : $"{issuer}/{suffix}";
    }

    private sealed class ResolvedOidcEndpoints
    {
        public string Issuer { get; init; } = string.Empty;
        public string AuthorizationEndpoint { get; init; } = string.Empty;
        public string TokenEndpoint { get; init; } = string.Empty;
        public string UserInfoEndpoint { get; init; } = string.Empty;
        public string JwksUri { get; init; } = string.Empty;
    }
}

public class JwksService : IJwksService
{
    private readonly OidcSigningKeyMaterial _keyMaterial;

    public JwksService(OidcSigningKeyMaterial keyMaterial)
    {
        _keyMaterial = keyMaterial;
    }

    public Task<Blocks.Genesis.Auth.JwksResponse> GetKeysAsync()
    {
        var parameters = _keyMaterial.Rsa.ExportParameters(false);
        return Task.FromResult(new Blocks.Genesis.Auth.JwksResponse
        {
            Keys =
            [
                new Blocks.Genesis.Auth.JwkKey
                {
                    Kid = _keyMaterial.SecurityKey.KeyId ?? string.Empty,
                    N = Base64UrlEncoder.Encode(parameters.Modulus),
                    E = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            ]
        });
    }
}
