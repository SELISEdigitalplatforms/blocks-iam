using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
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
            jwtClaims.Add(new Claim("aud", claims.ClientId));
        }

        if (includeNonce && !string.IsNullOrWhiteSpace(claims.Nonce))
        {
            jwtClaims.Add(new Claim(JwtRegisteredClaimNames.Nonce, claims.Nonce));
        }

        foreach (var resource in claims.Resources)
        {
            jwtClaims.Add(new Claim("resource", resource));
        }

        foreach (var permission in claims.Permissions)
        {
            jwtClaims.Add(new Claim("permission", permission));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: claims.ClientId,
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

    public DiscoveryService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<Blocks.Genesis.Auth.DiscoveryMetadata> GetMetadataAsync()
    {
        var issuer = GetIssuer();
        return Task.FromResult(new Blocks.Genesis.Auth.DiscoveryMetadata
        {
            Issuer = issuer,
            AuthorizationEndpoint = $"{issuer}/api/oidc/authorize",
            TokenEndpoint = $"{issuer}/api/oidc/token",
            UserInfoEndpoint = $"{issuer}/api/auth/userinfo",
            JwksUri = $"{issuer}/.well-known/jwks.json"
        });
    }

    public Task<Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata> GetAuthorizationServerMetadataAsync()
    {
        var issuer = GetIssuer();
        return Task.FromResult(new Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata
        {
            Issuer = issuer,
            AuthorizationEndpoint = $"{issuer}/api/oidc/authorize",
            TokenEndpoint = $"{issuer}/api/oidc/token",
            JwksUri = $"{issuer}/.well-known/jwks.json"
        });
    }

    private string GetIssuer()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            return "https://localhost";
        }

        return $"{request.Scheme}://{request.Host.Value}";
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