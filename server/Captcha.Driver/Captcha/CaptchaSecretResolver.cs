using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Reads the captcha secret from Blocks Secrets on every call; never caches it.
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
    /// <summary>
    /// Actor recorded on the secret store's audit row. A named identity rather than an empty one,
    /// so a captcha resolution is distinguishable from an unattributed read.
    /// </summary>
    internal const string ServiceUserId = "blocks-iam-captcha";

    /// <summary>Marks the request as this resolver's own, not a real address: <c>internal://captcha/resolve-secret</c>.</summary>
    private static readonly string ServiceRequestUri = new UriBuilder("internal", "captcha") { Path = "resolve-secret" }.Uri.ToString();
    private const string DefaultOrganizationId = "default";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CaptchaSecretResolver> _logger;

    public CaptchaSecretResolver(
        IServiceScopeFactory scopeFactory,
        ILogger<CaptchaSecretResolver> logger)
    {
        _scopeFactory = scopeFactory;
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

        var organizationId = string.IsNullOrWhiteSpace(context!.OrganizationId)
            ? DefaultOrganizationId
            : context.OrganizationId;

        var value = await ReadFromStoreAsync(secretId, tenantId, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrEmpty(value) ? null : value;
    }

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

        // Nothing is stored on any path.
        return null;
    }

    private async Task<string?> ReadValueAsync(string secretId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var secretService = scope.ServiceProvider.GetRequiredService<ISecretService>();

        return await secretService.GetValueAsync(secretId, cancellationToken).ConfigureAwait(false);
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
