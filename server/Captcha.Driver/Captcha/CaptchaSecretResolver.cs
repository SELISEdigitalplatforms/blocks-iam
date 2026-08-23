using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Key Vault backed <see cref="ICaptchaSecretResolver"/>, with a short-lived cache in front.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> Registered as a singleton, but <see cref="ISecretService"/> and its
/// collaborators are registered <i>scoped</i> by <c>AddBlocksSecrets()</c> because they read the
/// request-scoped <see cref="BlocksContext"/>. Taking one as a constructor dependency would be a
/// captive dependency: the first tenant to resolve it would be served to every later caller.
/// A scope is therefore created per call and the service resolved from it.
/// </para>
/// <para>
/// <b>Context.</b> The secret package refuses to act without an authenticated context by design,
/// and captcha verification runs on anonymous endpoints. The call is made inside a synthesized
/// service context carrying the ambient tenant — the same pattern blocks-release uses for its
/// queue consumers. This is safe on an anonymous request because <see cref="BlocksContext.GetContext"/>
/// only prefers the HTTP identity when that identity is authenticated; otherwise it falls through
/// to the AsyncLocal value set here. On an authenticated request the real caller wins instead,
/// which is a better audit actor, not a worse one.
/// </para>
/// </remarks>
public sealed class CaptchaSecretResolver : ICaptchaSecretResolver
{
    internal const string CacheKeyPrefix = "captcha:secret:";

    /// <summary>Cache lifetime, in seconds. Deliberately fixed rather than configurable.</summary>
    internal const long CacheTtlSeconds = 1800;

    /// <summary>
    /// Actor recorded on the secret store's audit row. A named identity rather than an empty one,
    /// so a captcha resolution is distinguishable from an unattributed read.
    /// </summary>
    internal const string ServiceUserId = "blocks-iam-captcha";

    private const string ServiceRequestUri = "internal://captcha/resolve-secret";
    private const string DefaultOrganizationId = "default";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheClient _cache;
    private readonly ILogger<CaptchaSecretResolver> _logger;

    public CaptchaSecretResolver(
        IServiceScopeFactory scopeFactory,
        ICacheClient cache,
        ILogger<CaptchaSecretResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(string? secretId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            return null;
        }

        var context = BlocksContext.GetContext();
        var tenantId = context?.TenantId;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // Fail closed. The secret store separates tenants by a TenantId filter alone, so a
            // guessed or defaulted tenant would be worse than not resolving.
            _logger.LogWarning(
                "Captcha secret {SecretId} cannot be resolved: no tenant on the ambient context.",
                secretId);
            return null;
        }

        var cacheKey = BuildCacheKey(tenantId, secretId);

        var cached = await TryReadCacheAsync(cacheKey).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var organizationId = string.IsNullOrWhiteSpace(context!.OrganizationId)
            ? DefaultOrganizationId
            : context.OrganizationId;

        var value = await ReadFromStoreAsync(secretId, tenantId, organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        await TryWriteCacheAsync(cacheKey, value).ConfigureAwait(false);
        return value;
    }

    internal static string BuildCacheKey(string tenantId, string secretId) =>
        $"{CacheKeyPrefix}{tenantId}:{secretId}";

    private async Task<string?> ReadFromStoreAsync(
        string secretId,
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await BlocksContext.ExecuteInContext(
                BuildServiceContext(tenantId, organizationId),
                () => ReadValueAsync(secretId, cancellationToken)).ConfigureAwait(false);
        }
        catch (SecretNotFoundException ex)
        {
            _logger.LogWarning(ex, "Captcha secret {SecretId} was not found in the secret store.", secretId);
        }
        catch (SecretAccessDeniedException ex)
        {
            _logger.LogWarning(ex, "Access to captcha secret {SecretId} was denied.", secretId);
        }
        catch (SecretStateException ex)
        {
            _logger.LogWarning(ex, "Captcha secret {SecretId} is not readable in its current state.", secretId);
        }
        catch (SecretVaultException ex)
        {
            _logger.LogError(ex, "Reading captcha secret {SecretId} from the vault failed.", secretId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected failure resolving captcha secret {SecretId}.", secretId);
        }

        // Nothing is cached on any of these paths on purpose: a vault outage would otherwise
        // lock captcha verification out for the whole TTL after a single failure.
        return null;
    }

    private async Task<string?> ReadValueAsync(string secretId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var secretService = scope.ServiceProvider.GetRequiredService<ISecretService>();

        return await secretService.GetValueAsync(secretId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TryReadCacheAsync(string cacheKey)
    {
        try
        {
            return await _cache.GetStringValueAsync(cacheKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A cache failure is a miss, never a request failure.
            _logger.LogWarning(ex, "Reading the captcha secret cache failed; treating as a miss.");
            return null;
        }
    }

    private async Task TryWriteCacheAsync(string cacheKey, string value)
    {
        try
        {
            await _cache.AddStringValueAsync(cacheKey, value, CacheTtlSeconds).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The value is already resolved; failing to cache it only costs a later round-trip.
            _logger.LogWarning(ex, "Writing the captcha secret cache failed.");
        }
    }

    private static BlocksContext BuildServiceContext(string tenantId, string organizationId) =>
        BlocksContext.Create(
            tenantId: tenantId,
            roles: [],
            userId: ServiceUserId,
            isAuthenticated: true,
            requestUri: ServiceRequestUri,
            organizationId: organizationId,
            expireOn: DateTime.UtcNow.AddMinutes(5),
            email: string.Empty,
            permissions: [],
            userName: ServiceUserId,
            phoneNumber: string.Empty,
            displayName: ServiceUserId,
            oauthToken: string.Empty,
            originalTenantId: tenantId);
}
