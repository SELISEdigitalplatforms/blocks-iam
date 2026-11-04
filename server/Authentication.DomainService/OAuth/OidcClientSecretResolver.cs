using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    /// <summary>
    /// Reads and writes the secret of an OIDC client.
    /// </summary>
    /// <remarks>
    /// Reference rule, as for client credentials and identity providers: a non-empty
    /// <c>ClientSecretReference</c> is authoritative and its value lives in Blocks Secrets; an empty
    /// one means <c>ClientSecret</c> still holds the plaintext. When a reference is set the plaintext
    /// field is never used, not even as a fallback when the read fails.
    /// </remarks>
    public interface IOidcClientSecretResolver
    {
        /// <summary>
        /// The secret a token request's <c>client_secret</c> is compared with.
        /// </summary>
        /// <exception cref="OidcClientSecretUnavailableException">The referenced secret cannot be read.</exception>
        Task<string?> GetSecretAsync(OidcClientRegistration client, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores <paramref name="value"/> as a new <c>service</c> secret and returns its reference.
        /// Secret-store exceptions propagate.
        /// </summary>
        Task<string> StoreAsync(OidcClientRegistration client, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the secret: rotates the existing reference in place, or stores a new secret when
        /// there is no reference or the old one is missing or deleted
        /// (<see cref="SecretNotFoundException"/> / <see cref="SecretStateException"/>).
        /// Other secret-store exceptions propagate.
        /// </summary>
        Task<(string Reference, bool Created)> ReplaceAsync(OidcClientRegistration client, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-deletes a secret. Apart from a null <paramref name="client"/> and cancellation it
        /// never throws: not-found and state failures are ignored, anything else is logged as a warning.
        /// </summary>
        Task RetireAsync(OidcClientRegistration client, string? reference, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A referenced OIDC client secret could not be read. Carries the client id and the failure
    /// class only: never the value, the reference id or the underlying exception, whose message
    /// can name the reference id.
    /// </summary>
    public sealed class OidcClientSecretUnavailableException : Exception
    {
        public OidcClientSecretUnavailableException(string? clientId, string failureClass)
            : base($"The secret of OIDC client '{clientId}' is unavailable ({failureClass}).")
        {
            ClientId = clientId;
            FailureClass = failureClass;
        }

        public string? ClientId { get; }

        /// <summary><see cref="OidcClientSecretResolver.NoTenantFailure"/> or the type name of the exception that failed the read.</summary>
        public string FailureClass { get; }
    }

    /// <summary>The outcome of checking a presented client secret against an OIDC client.</summary>
    public enum OidcClientSecretCheck
    {
        Matches,
        DoesNotMatch,

        /// <summary>The referenced secret cannot be read; the caller answers 503, never "wrong secret".</summary>
        Unavailable,
    }

    /// <summary>The token-endpoint side of <see cref="IOidcClientSecretResolver"/>, shared by every client-authentication site.</summary>
    public static class OidcClientSecretResolverExtensions
    {
        public const string UnavailableError = "temporarily_unavailable";
        public const string UnavailableDescription = "The client secret cannot be checked right now. Try again later.";

        /// <summary>
        /// Compares <paramref name="presentedSecret"/> with the client's secret in constant time. A
        /// blank stored or presented secret never matches. The resolver has already logged an
        /// unreadable secret.
        /// </summary>
        public static async Task<OidcClientSecretCheck> CheckSecretAsync(
            this IOidcClientSecretResolver resolver, OidcClientRegistration client, string? presentedSecret, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(client);

            string? storedSecret;
            try
            {
                storedSecret = await resolver.GetSecretAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (OidcClientSecretUnavailableException)
            {
                return OidcClientSecretCheck.Unavailable;
            }

            return OidcClientValidator.SecretsMatch(presentedSecret, storedSecret)
                ? OidcClientSecretCheck.Matches
                : OidcClientSecretCheck.DoesNotMatch;
        }
    }

    /// <summary>
    /// Blocks Secrets access for OIDC client secrets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same rules as <see cref="ClientCredentialSecretResolver"/>: a singleton that creates a DI
    /// scope per call for the scoped <see cref="ISecretService"/>, typed failures, no cache.
    /// </para>
    /// <para>
    /// <b>Read context.</b> The token, introspection and revocation endpoints are anonymous, and the
    /// secret package refuses an unauthenticated context. Unless the caller is already
    /// authenticated, the read runs in a synthesized authenticated service context carrying the
    /// ambient tenant, which is the tenant whose <c>OidcClientRegistrations</c> was just read. A
    /// <c>service</c> secret is checked against the tenant and its status only, so the
    /// organization of that context does not decide the read.
    /// </para>
    /// <para>
    /// <b>Write context.</b> Writes run as the console caller (impersonated into the project).
    /// </para>
    /// </remarks>
    public sealed class OidcClientSecretResolver : IOidcClientSecretResolver
    {
        public const string SecretName = "oidc-client-secret";
        public const string NoTenantFailure = "no_tenant";

        /// <summary>Actor recorded on the secret store's audit row for a token-request read.</summary>
        public const string ServiceUserId = "blocks-iam-oidc-client";

        /// <summary>Marks the request as this resolver's own, not a real address.</summary>
        private static readonly string ServiceRequestUri = new UriBuilder("internal", "oidc-client") { Path = "resolve-secret" }.Uri.ToString();
        private const string DefaultOrganizationId = "default";
        private const int MaxNameInDescription = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OidcClientSecretResolver> _logger;

        public OidcClientSecretResolver(IServiceScopeFactory scopeFactory, ILogger<OidcClientSecretResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // A whitespace-only reference counts as a reference and fails closed instead of falling
        // back to the plaintext field.
        public Task<string?> GetSecretAsync(OidcClientRegistration client, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);

            return string.IsNullOrEmpty(client.ClientSecretReference)
                ? Task.FromResult(client.ClientSecret)
                : ReadAsync(client, client.ClientSecretReference, cancellationToken);
        }

        private async Task<string?> ReadAsync(OidcClientRegistration client, string reference, CancellationToken cancellationToken)
        {
            var ambient = BlocksContext.GetContext();
            var tenantId = ambient?.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                // Fail closed: the store separates tenants by TenantId alone, so a defaulted
                // tenant would be worse than not resolving.
                throw Unavailable(client, NoTenantFailure);
            }

            try
            {
                return ambient!.IsAuthenticated
                    ? await GetValueInScopeAsync(reference, cancellationToken).ConfigureAwait(false)
                    : await ReadAsServiceAsync(ambient, tenantId, reference, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Secret* messages carry the reference id; only the type is surfaced.
                throw Unavailable(client, ex.GetType().Name);
            }
        }

        private async Task<string> ReadAsServiceAsync(BlocksContext ambient, string tenantId, string reference, CancellationToken cancellationToken)
        {
            // The ambient context is unauthenticated here, so GetContext() returned the AsyncLocal
            // value: that is what is written back.
            try
            {
                var organizationId = string.IsNullOrWhiteSpace(ambient.OrganizationId) ? DefaultOrganizationId : ambient.OrganizationId;

                // changeContext: true makes GetContext() return this context whatever the HTTP
                // identity is, so the read does not depend on that identity being unauthenticated.
                BlocksContext.SetContext(BuildServiceContext(tenantId, organizationId), true);

                return await GetValueInScopeAsync(reference, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Restores the anonymous context for a continuation that runs without the caller's
                // ExecutionContext; ambient is never null here (the tenant check above).
                BlocksContext.SetContext(ambient, true);
            }
        }

        private async Task<string> GetValueInScopeAsync(string reference, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            return await secrets.GetValueAsync(reference, cancellationToken).ConfigureAwait(false);
        }

        private OidcClientSecretUnavailableException Unavailable(OidcClientRegistration client, string failureClass)
        {
            _logger.LogWarning(
                "The secret of OIDC client {ClientId} is unavailable: {FailureClass}.",
                client.ClientId, failureClass);

            return new OidcClientSecretUnavailableException(client.ClientId, failureClass);
        }

        public async Task<string> StoreAsync(OidcClientRegistration client, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            return await secrets.SetAsync(BuildRequest(client, value), cancellationToken).ConfigureAwait(false);
        }

        public async Task<(string Reference, bool Created)> ReplaceAsync(OidcClientRegistration client, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            var reference = client.ClientSecretReference;

            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            if (!string.IsNullOrEmpty(reference))
            {
                try
                {
                    // A Locked secret rotates successfully and stays Locked (Secrets 4.0.3), so token
                    // requests keep failing until an admin unlocks it on the Secrets page.
                    await secrets.RotateAsync(reference, new RotateSecretRequest { Value = value }, cancellationToken).ConfigureAwait(false);
                    return (reference, false);
                }
                catch (Exception ex) when (ex is SecretNotFoundException or SecretStateException)
                {
                    // The old secret is unusable and left as it is; a new one takes its place.
                    _logger.LogInformation(
                        "The secret of OIDC client {ClientId} could not be rotated ({FailureClass}); storing a new secret.",
                        client.ClientId, ex.GetType().Name);
                }
            }

            var id = await secrets.SetAsync(BuildRequest(client, value), cancellationToken).ConfigureAwait(false);
            return (id, true);
        }

        public async Task RetireAsync(OidcClientRegistration client, string? reference, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);

            if (string.IsNullOrEmpty(reference))
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
                await secrets.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SecretNotFoundException or SecretStateException)
            {
                // Already gone or already deleted: nothing to retire.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Retiring the secret of OIDC client {ClientId} failed: {FailureClass}. The secret is left orphaned.",
                    client.ClientId, ex.GetType().Name);
            }
        }

        private static SetSecretRequest BuildRequest(OidcClientRegistration client, string value) =>
            new()
            {
                Name = SecretName,
                Type = SecretTypes.Service,
                Description = BuildDescription(client),
                OrganizationId = client.OrganizationId,
                Value = value,
            };

        private static string BuildDescription(OidcClientRegistration client)
        {
            var name = client.ClientName ?? string.Empty;
            if (name.Length > MaxNameInDescription)
            {
                name = name[..MaxNameInDescription];
            }

            var tenantId = BlocksContext.GetContext()?.TenantId;
            return string.IsNullOrWhiteSpace(tenantId)
                ? $"OIDC client {name} ({client.ClientId}): client secret"
                : $"OIDC client {name} ({client.ClientId}, tenant {tenantId}): client secret";
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
