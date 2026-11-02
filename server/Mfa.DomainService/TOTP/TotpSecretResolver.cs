using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mfa.DomainService.TOTP
{
    /// <summary>
    /// Reads and writes TOTP secrets in Blocks Secrets.
    /// </summary>
    /// <remarks>
    /// The value lives only in the secret store; <c>UserTotpDetail</c> keeps the reference. There is
    /// no plaintext path, no cache and no fallback: a failed read fails the check (owner 2026-11-01).
    /// </remarks>
    public interface ITotpSecretResolver
    {
        /// <summary>The secret behind <paramref name="reference"/>.</summary>
        /// <exception cref="TotpSecretUnavailableException">The secret cannot be read.</exception>
        Task<string> GetAsync(string userId, string reference, CancellationToken cancellationToken = default);

        /// <summary>Stores <paramref name="value"/> as a new <c>service</c> secret and returns its reference.</summary>
        /// <exception cref="TotpSecretUnavailableException">The secret cannot be stored.</exception>
        Task<string> StoreAsync(string userId, string? organizationId, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Permanently deletes a secret (delete, then purge), so no deleted record stays behind. Never
        /// throws apart from cancellation: a secret already gone is ignored, anything else is logged as a warning.
        /// </summary>
        Task RetireAsync(string userId, string? reference, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A TOTP secret could not be read. Carries the user id and the failure class only: never the
    /// value, the reference id or the underlying exception, whose message can name the reference id.
    /// </summary>
    public sealed class TotpSecretUnavailableException : Exception
    {
        public TotpSecretUnavailableException(string userId, string failureClass)
            : base($"The TOTP secret of user '{userId}' is unavailable ({failureClass}).")
        {
            FailureClass = failureClass;
        }

        public string FailureClass { get; }
    }

    /// <summary>
    /// Blocks Secrets access for TOTP secrets.
    /// </summary>
    /// <remarks>
    /// Same rules as <c>ClientCredentialSecretResolver</c>: a singleton that creates a DI scope per
    /// call for the scoped <see cref="ISecretService"/>. The sign-in check runs before the user has a
    /// token, and the secret package refuses an unauthenticated context, so an anonymous call runs in
    /// a synthesized service context carrying the ambient tenant. A <c>service</c> secret is checked
    /// against the tenant and its status only.
    /// </remarks>
    public sealed class TotpSecretResolver : ITotpSecretResolver
    {
        public const string SecretName = "totp-secret";
        public const string NoTenantFailure = "no_tenant";

        /// <summary>Actor recorded on the secret store's audit row for an anonymous call.</summary>
        public const string ServiceUserId = "blocks-iam-totp";

        private static readonly string ServiceRequestUri = new UriBuilder("internal", "totp") { Path = "resolve-secret" }.Uri.ToString();
        private const string DefaultOrganizationId = "default";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TotpSecretResolver> _logger;

        public TotpSecretResolver(IServiceScopeFactory scopeFactory, ILogger<TotpSecretResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task<string> GetAsync(string userId, string reference, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference);
            return GetCoreAsync(userId, reference, cancellationToken);
        }

        private async Task<string> GetCoreAsync(string userId, string reference, CancellationToken cancellationToken)
        {
            try
            {
                var value = await InContextAsync(userId, secrets => secrets.GetValueAsync(reference, cancellationToken)).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(value) ? throw Unavailable(userId, "empty_value") : value;
            }
            catch (TotpSecretUnavailableException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Secret* messages carry the reference id; only the type is surfaced.
                throw Unavailable(userId, ex.GetType().Name);
            }
        }

        public Task<string> StoreAsync(string userId, string? organizationId, string value, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            return StoreCoreAsync(userId, organizationId, value, cancellationToken);
        }

        private async Task<string> StoreCoreAsync(string userId, string? organizationId, string value, CancellationToken cancellationToken)
        {
            var request = new SetSecretRequest
            {
                Name = SecretName,
                Type = SecretTypes.Service,
                Description = $"TOTP secret of user {userId}",
                OrganizationId = string.IsNullOrWhiteSpace(organizationId) ? DefaultOrganizationId : organizationId,
                Value = value,
            };

            try
            {
                return await InContextAsync(userId, secrets => secrets.SetAsync(request, cancellationToken)).ConfigureAwait(false);
            }
            catch (TotpSecretUnavailableException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw Unavailable(userId, ex.GetType().Name);
            }
        }

        public async Task RetireAsync(string userId, string? reference, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return;
            }

            try
            {
                await InContextAsync(userId, async secrets =>
                {
                    try
                    {
                        await secrets.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
                    }
                    catch (SecretStateException)
                    {
                        // Already deleted (an earlier retire stopped before the purge): purge it now.
                    }

                    // Purge needs the deleted state; it removes the value and the metadata (owner 2026-11-02).
                    await secrets.PurgeAsync(reference, cancellationToken).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
            }
            catch (SecretNotFoundException)
            {
                // Already gone: nothing to retire.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Type only: Secret* messages carry the reference id.
                _logger.LogWarning( // NOSONAR S6667 — type only
                    "Retiring the TOTP secret of user {UserId} failed: {FailureClass}. The secret is left orphaned.",
                    userId, ex.GetType().Name);
            }
        }

        private async Task<T> InContextAsync<T>(string userId, Func<ISecretService, Task<T>> call)
        {
            var ambient = BlocksContext.GetContext();
            var tenantId = ambient?.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                // Fail closed: the store separates tenants by TenantId alone.
                throw Unavailable(userId, NoTenantFailure);
            }

            if (ambient!.IsAuthenticated)
            {
                return await InScopeAsync(call).ConfigureAwait(false);
            }

            try
            {
                var organizationId = string.IsNullOrWhiteSpace(ambient.OrganizationId) ? DefaultOrganizationId : ambient.OrganizationId;
                BlocksContext.SetContext(BuildServiceContext(tenantId, organizationId), true);
                return await InScopeAsync(call).ConfigureAwait(false);
            }
            finally
            {
                BlocksContext.SetContext(ambient, true);
            }
        }

        private async Task<T> InScopeAsync<T>(Func<ISecretService, Task<T>> call)
        {
            using var scope = _scopeFactory.CreateScope();
            return await call(scope.ServiceProvider.GetRequiredService<ISecretService>()).ConfigureAwait(false);
        }

        private TotpSecretUnavailableException Unavailable(string userId, string failureClass)
        {
            _logger.LogWarning("The TOTP secret of user {UserId} is unavailable: {FailureClass}.", userId, failureClass);
            return new TotpSecretUnavailableException(userId, failureClass);
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
}
