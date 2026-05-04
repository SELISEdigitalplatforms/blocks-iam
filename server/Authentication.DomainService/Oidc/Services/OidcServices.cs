using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using DomainService.OAuth;
using DomainService.Utilities;
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
    private readonly ITenants _tenants;
    private readonly ICacheClient _cacheClient;
    private readonly ICryptoService _cryptoService;
    private readonly ICertificateProviderFactory _certificateProviderFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TokenGenerationService(
        OidcSigningKeyMaterial keyMaterial,
        ITenants tenants,
        ICacheClient cacheClient,
        ICryptoService cryptoService,
        ICertificateProviderFactory certificateProviderFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        _keyMaterial = keyMaterial;
        _tenants = tenants;
        _cacheClient = cacheClient;
        _cryptoService = cryptoService;
        _certificateProviderFactory = certificateProviderFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<string> GenerateIdTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds)
    {
        return GenerateTokenAsync(claims, issuer, expiresInSeconds, includeNonce: true);
    }

    public Task<string> GenerateAccessTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds)
    {
        return GenerateTokenAsync(claims, issuer, expiresInSeconds, includeNonce: false);
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

    private async Task<string> GenerateTokenAsync(Blocks.Genesis.Auth.OidcClaims claims, string issuer, int expiresInSeconds, bool includeNonce)
    {
        var now = DateTime.UtcNow;
        var tenant = ResolveTenant(claims.TenantId);
        var resolvedIssuer = !string.IsNullOrWhiteSpace(tenant?.JwtTokenParameters?.Issuer)
            ? tenant.JwtTokenParameters.Issuer
            : issuer;
        var signingCredentials = await ResolveSigningCredentialsAsync(tenant) ?? _keyMaterial.SigningCredentials;

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
            issuer: resolvedIssuer,
            audience: claims.Audience ?? claims.ClientId,
            claims: jwtClaims,
            notBefore: now,
            expires: now.AddSeconds(expiresInSeconds),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private Tenant? ResolveTenant(string? tenantId)
    {
        var resolvedTenantId = !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : ResolveTenantIdFromContext();

        return string.IsNullOrWhiteSpace(resolvedTenantId)
            ? null
            : _tenants.GetTenantByID(resolvedTenantId);
    }

    private string? ResolveTenantIdFromContext()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        var queryTenantId = request?.Query["tenant_id"].ToString();
        if (!string.IsNullOrWhiteSpace(queryTenantId))
        {
            return queryTenantId;
        }

        var headerTenantId = request?.Headers["X-Blocks-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(headerTenantId))
        {
            return headerTenantId;
        }

        return BlocksContext.GetContext()?.TenantId;
    }

    private async Task<SigningCredentials?> ResolveSigningCredentialsAsync(Tenant? tenant)
    {
        if (tenant?.JwtTokenParameters == null)
        {
            return null;
        }

        var certificateBytes = await GetOrRetrievePrivateCertificateAsync(tenant);
        if (certificateBytes == null || certificateBytes.Length == 0)
        {
            return null;
        }

        var certificate = LoadCertificate(certificateBytes, tenant.JwtTokenParameters.PrivateCertificatePassword);
        var rsa = certificate.GetRSAPrivateKey();
        if (rsa == null)
        {
            return null;
        }

        var securityKey = new RsaSecurityKey(rsa)
        {
            KeyId = ResolveKeyId(certificate)
        };

        return new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
    }

    private async Task<byte[]?> GetOrRetrievePrivateCertificateAsync(Tenant tenant)
    {
        var key = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
        var cachedCertificate = _cacheClient.CacheDatabase().StringGet(key);
        if (cachedCertificate.HasValue)
        {
            return cachedCertificate!;
        }

        var provider = _certificateProviderFactory.GetProvider(tenant.JwtTokenParameters?.CertificateStorageType ?? CertificateStorageType.Azure);
        var certificate = await provider.GetCertificateAsync(key);

        if (certificate.Length > 0)
        {
            _cacheClient.CacheDatabase().StringSet(key, certificate, ResolveCacheLifetime(tenant));
        }

        return certificate;
    }

    private static TimeSpan ResolveCacheLifetime(Tenant tenant)
    {
        var tokenParameters = tenant.JwtTokenParameters;
        var expirationDays = tokenParameters?.CertificateValidForNumberOfDays - (DateTime.UtcNow - tokenParameters?.IssueDate)?.Days - 1;
        return TimeSpan.FromDays(Math.Max(expirationDays ?? 0, 0));
    }

    private static X509Certificate2 LoadCertificate(byte[] certificateBytes, string? password)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(certificateBytes, password, X509KeyStorageFlags.Exportable);
        }
        catch (CryptographicException)
        {
            return X509CertificateLoader.LoadCertificate(certificateBytes);
        }
    }

    private static string ResolveKeyId(X509Certificate2 certificate)
    {
        return Base64UrlEncoder.Encode(certificate.Thumbprint ?? string.Empty);
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
    private const string DiscoveryCachePrefix = "oidcdiscovery::";
    private const string OAuthCachePrefix = "oidcoauth::";
    private static readonly TimeSpan DiscoveryCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ITenants _tenants;
    private readonly ICacheClient _cacheClient;

    public DiscoveryService(IHttpContextAccessor httpContextAccessor, IConfiguration configuration, ITenants tenants, ICacheClient cacheClient)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _tenants = tenants;
        _cacheClient = cacheClient;
    }

    public async Task<Blocks.Genesis.Auth.DiscoveryMetadata> GetMetadataAsync()
    {
        var tenantId = ResolveTenantId();
        var cacheKey = $"{DiscoveryCachePrefix}{tenantId ?? "_default"}";

        var cached = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<Blocks.Genesis.Auth.DiscoveryMetadata>((string)cached!)!;
        }

        var endpoints = ResolveEndpoints(tenantId);
        var metadata = new Blocks.Genesis.Auth.DiscoveryMetadata
        {
            Issuer = endpoints.Issuer,
            AuthorizationEndpoint = endpoints.AuthorizationEndpoint,
            TokenEndpoint = endpoints.TokenEndpoint,
            UserInfoEndpoint = endpoints.UserInfoEndpoint,
            JwksUri = endpoints.JwksUri
        };

        await _cacheClient.CacheDatabase().StringSetAsync(cacheKey, JsonSerializer.Serialize(metadata), DiscoveryCacheTtl);
        return metadata;
    }

    public async Task<Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata> GetAuthorizationServerMetadataAsync()
    {
        var tenantId = ResolveTenantId();
        var cacheKey = $"{OAuthCachePrefix}{tenantId ?? "_default"}";

        var cached = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata>((string)cached!)!;
        }

        var endpoints = ResolveEndpoints(tenantId);
        var metadata = new Blocks.Genesis.Auth.OAuthAuthorizationServerMetadata
        {
            Issuer = endpoints.Issuer,
            AuthorizationEndpoint = endpoints.AuthorizationEndpoint,
            TokenEndpoint = endpoints.TokenEndpoint,
            JwksUri = endpoints.JwksUri
        };

        await _cacheClient.CacheDatabase().StringSetAsync(cacheKey, JsonSerializer.Serialize(metadata), DiscoveryCacheTtl);
        return metadata;
    }

    private ResolvedOidcEndpoints ResolveEndpoints(string? tenantId)
    {
        var tenant = ResolveTenant(tenantId);
        var issuer = ResolveIssuer(tenant);
        var apiPrefix = ApplicationConfigurations.NormalizeApiRoutePrefixValue(_configuration["ApiRouting:Prefix"]);
        var serviceSegment = ResolveRequiredServiceName(_configuration);

        // Use path-based tenant selector for discovery and endpoint URLs.
        var tenantScopedPrefix = string.IsNullOrWhiteSpace(tenantId)
            ? Array.Empty<string>()
            : [tenantId];

        var jwksUri = string.IsNullOrWhiteSpace(tenantId)
            ? BuildUrl(issuer, ".well-known", "jwks.json")
            : BuildUrl(issuer, tenantId, ".well-known", "jwks.json");

        var authorizationEndpoint = BuildUrl(issuer, tenantScopedPrefix.Concat([apiPrefix, serviceSegment, "oidc", "authorize"]).ToArray());
        var tokenEndpoint = BuildUrl(issuer, tenantScopedPrefix.Concat([apiPrefix, serviceSegment, "oidc", "token"]).ToArray());
        var userInfoEndpoint = BuildUrl(issuer, tenantScopedPrefix.Concat([apiPrefix, serviceSegment, "auth", "userinfo"]).ToArray());

        return new ResolvedOidcEndpoints
        {
            Issuer = issuer,
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            UserInfoEndpoint = userInfoEndpoint,
            JwksUri = jwksUri
        };
    }

    private Tenant? ResolveTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? null : _tenants.GetTenantByID(tenantId);
    }

    private string? ResolveTenantId()
    {
        var request = _httpContextAccessor.HttpContext?.Request;

        // Path-based: /{tenant_id}/.well-known/... (RFC 8414 compliant)
        var routeTenantId = request?.RouteValues["tenant_id"]?.ToString();
        if (!string.IsNullOrWhiteSpace(routeTenantId))
        {
            return routeTenantId;
        }

        var queryTenantId = request?.Query["tenant_id"].ToString();
        if (!string.IsNullOrWhiteSpace(queryTenantId))
        {
            return queryTenantId;
        }

        var headerTenantId = request?.Headers["X-Blocks-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(headerTenantId))
        {
            return headerTenantId;
        }

        return BlocksContext.GetContext()?.TenantId;
    }

    private string ResolveIssuer(Tenant? tenant)
    {
        if (!string.IsNullOrWhiteSpace(tenant?.JwtTokenParameters?.Issuer)
            && Uri.TryCreate(tenant.JwtTokenParameters.Issuer, UriKind.Absolute, out _))
        {
            return tenant.JwtTokenParameters.Issuer.TrimEnd('/');
        }

        var requestIssuer = GetIssuerFromRequest();
        if (!string.IsNullOrWhiteSpace(requestIssuer))
        {
            return requestIssuer;
        }

        return GetConfiguredIssuerFallback();
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

    private string? GetIssuerFromRequest()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request != null)
        {
            var pathBase = request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
            return $"{request.Scheme}://{request.Host.Value}{pathBase}";
        }

        return null;
    }

    private string GetConfiguredIssuerFallback()
    {

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
    private static readonly HttpClient PublicCertificateHttpClient = new();

    private readonly OidcSigningKeyMaterial _keyMaterial;
    private readonly ITenants _tenants;
    private readonly ICacheClient _cacheClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICryptoService _cryptoService;
    private readonly ICertificateProviderFactory _certificateProviderFactory;

    public JwksService(
        OidcSigningKeyMaterial keyMaterial,
        ITenants tenants,
        ICacheClient cacheClient,
        IHttpContextAccessor httpContextAccessor,
        ICryptoService cryptoService,
        ICertificateProviderFactory certificateProviderFactory)
    {
        _keyMaterial = keyMaterial;
        _tenants = tenants;
        _cacheClient = cacheClient;
        _httpContextAccessor = httpContextAccessor;
        _cryptoService = cryptoService;
        _certificateProviderFactory = certificateProviderFactory;
    }

    public async Task<Blocks.Genesis.Auth.JwksResponse> GetKeysAsync()
    {
        var tenant = ResolveTenant();
        var certificate = tenant == null ? null : await GetPreferredCertificateAsync(tenant);
        if (certificate == null)
        {
            var fallbackParameters = _keyMaterial.Rsa.ExportParameters(false);
            return new Blocks.Genesis.Auth.JwksResponse
            {
                Keys =
                [
                    new Blocks.Genesis.Auth.JwkKey
                    {
                        Kid = _keyMaterial.SecurityKey.KeyId ?? string.Empty,
                        N = Base64UrlEncoder.Encode(fallbackParameters.Modulus),
                        E = Base64UrlEncoder.Encode(fallbackParameters.Exponent)
                    }
                ]
            };
        }

        using var rsa = certificate.GetRSAPublicKey();
        if (rsa == null)
        {
            throw new InvalidOperationException("The resolved tenant certificate does not contain an RSA public key.");
        }

        var parameters = rsa.ExportParameters(false);
        return new Blocks.Genesis.Auth.JwksResponse
        {
            Keys =
            [
                new Blocks.Genesis.Auth.JwkKey
                {
                    Kid = ResolveKeyId(certificate),
                    N = Base64UrlEncoder.Encode(parameters.Modulus),
                    E = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            ]
        };
    }

    private Tenant? ResolveTenant()
    {
        var request = _httpContextAccessor.HttpContext?.Request;

        // Path-based: /{tenant_id}/.well-known/... (RFC 8414 compliant)
        var tenantId = request?.RouteValues["tenant_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = request?.Query["tenant_id"].ToString();
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = request?.Headers["X-Blocks-Key"].ToString();
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = BlocksContext.GetContext()?.TenantId;
        }

        return string.IsNullOrWhiteSpace(tenantId) ? null : _tenants.GetTenantByID(tenantId);
    }

    private async Task<X509Certificate2?> GetPreferredCertificateAsync(Tenant tenant)
    {
        var publicCertificate = await GetPublicCertificateAsync(tenant);
        if (publicCertificate != null)
        {
            return publicCertificate;
        }

        var privateCertificateBytes = await GetPrivateCertificateBytesAsync(tenant);
        if (privateCertificateBytes == null || privateCertificateBytes.Length == 0)
        {
            return null;
        }

        return LoadCertificate(privateCertificateBytes, tenant.JwtTokenParameters?.PrivateCertificatePassword);
    }

    private async Task<X509Certificate2?> GetPublicCertificateAsync(Tenant tenant)
    {
        if (tenant.JwtTokenParameters == null)
        {
            return null;
        }

        var cacheKey = $"{IdpConstants.TenantTokenPublicCertificateCachePrefix}{tenant.TenantId}";
        var cachedCertificate = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
        if (cachedCertificate.HasValue)
        {
            return LoadCertificate(cachedCertificate!, tenant.JwtTokenParameters.PublicCertificatePassword);
        }

        var downloadedCertificate = await DownloadPublicCertificateAsync(tenant.JwtTokenParameters.PublicCertificatePath);
        if (downloadedCertificate.Length == 0)
        {
            return null;
        }

        _cacheClient.CacheDatabase().StringSet(cacheKey, downloadedCertificate, ResolveCacheLifetime(tenant));
        return LoadCertificate(downloadedCertificate, tenant.JwtTokenParameters.PublicCertificatePassword);
    }

    private async Task<byte[]> GetPrivateCertificateBytesAsync(Tenant tenant)
    {
        var key = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
        var cachedCertificate = _cacheClient.CacheDatabase().StringGet(key);
        if (cachedCertificate.HasValue)
        {
            return cachedCertificate!;
        }

        var provider = _certificateProviderFactory.GetProvider(tenant.JwtTokenParameters?.CertificateStorageType ?? CertificateStorageType.Azure);
        var certificate = await provider.GetCertificateAsync(key);
        if (certificate.Length > 0)
        {
            _cacheClient.CacheDatabase().StringSet(key, certificate, ResolveCacheLifetime(tenant));
        }

        return certificate;
    }

    private static async Task<byte[]> DownloadPublicCertificateAsync(string? publicCertificatePath)
    {
        if (string.IsNullOrWhiteSpace(publicCertificatePath))
        {
            return Array.Empty<byte>();
        }

        if (Uri.TryCreate(publicCertificatePath, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile && File.Exists(uri.LocalPath))
            {
                return await File.ReadAllBytesAsync(uri.LocalPath);
            }

            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return await PublicCertificateHttpClient.GetByteArrayAsync(uri);
            }
        }

        if (File.Exists(publicCertificatePath))
        {
            return await File.ReadAllBytesAsync(publicCertificatePath);
        }

        return Array.Empty<byte>();
    }

    private static TimeSpan ResolveCacheLifetime(Tenant tenant)
    {
        var tokenParameters = tenant.JwtTokenParameters;
        var expirationDays = tokenParameters?.CertificateValidForNumberOfDays - (DateTime.UtcNow - tokenParameters?.IssueDate)?.Days - 1;
        return TimeSpan.FromDays(Math.Max(expirationDays ?? 0, 0));
    }

    private static X509Certificate2 LoadCertificate(byte[] certificateBytes, string? password)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(certificateBytes, password, X509KeyStorageFlags.Exportable);
        }
        catch (CryptographicException)
        {
            return X509CertificateLoader.LoadCertificate(certificateBytes);
        }
    }

    private static string ResolveKeyId(X509Certificate2 certificate)
    {
        return Base64UrlEncoder.Encode(certificate.Thumbprint ?? string.Empty);
    }
}
