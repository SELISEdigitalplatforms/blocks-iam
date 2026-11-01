using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    /// <summary>
    /// Reads and writes the secret of a client credential.
    /// </summary>
    /// <remarks>
    /// Reference rule, as for identity providers: a non-empty <c>ClientSecretReference</c> is
    /// authoritative and its value lives in Blocks Secrets; an empty one means <c>ClientSecret</c>
    /// still holds the plaintext. When a reference is set the plaintext field is never used, not
    /// even as a fallback when the read fails.
    /// </remarks>
    public interface IClientCredentialSecretResolver
    {
        /// <summary>
        /// The secret a token request's <c>client_secret</c> is compared with.
        /// </summary>
        /// <exception cref="ClientCredentialSecretUnavailableException">The referenced secret cannot be read.</exception>
        Task<string?> GetSecretAsync(ClientCredential credential, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores <paramref name="value"/> as a new <c>service</c> secret and returns its reference.
        /// Secret-store exceptions propagate.
        /// </summary>
        Task<string> StoreAsync(ClientCredential credential, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the secret: rotates the existing reference in place, or stores a new secret when
        /// there is no reference or the old one is missing or deleted
        /// (<see cref="SecretNotFoundException"/> / <see cref="SecretStateException"/>).
        /// Other secret-store exceptions propagate.
        /// </summary>
        Task<(string Reference, bool Created)> ReplaceAsync(ClientCredential credential, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-deletes a secret. Apart from a null <paramref name="credential"/> and cancellation it
        /// never throws: not-found and state failures are ignored, anything else is logged as a warning.
        /// </summary>
        Task RetireAsync(ClientCredential credential, string? reference, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A referenced client-credential secret could not be read. Carries the client id and the
    /// failure class only: never the value, the reference id or the underlying exception, whose
    /// message can name the reference id.
    /// </summary>
    public sealed class ClientCredentialSecretUnavailableException : Exception
    {
        public ClientCredentialSecretUnavailableException(string? clientId, string failureClass)
            : base($"The secret of client credential '{clientId}' is unavailable ({failureClass}).")
        {
            ClientId = clientId;
            FailureClass = failureClass;
        }

        public string? ClientId { get; }

        /// <summary><see cref="ClientCredentialSecretResolver.NoTenantFailure"/> or the type name of the exception that failed the read.</summary>
        public string FailureClass { get; }
    }

    /// <summary>
    /// Blocks Secrets access for client-credential secrets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same rules as <see cref="IdentityProviderSecretResolver"/>: a singleton that creates a DI
    /// scope per call for the scoped <see cref="ISecretService"/>, typed failures, no cache.
    /// </para>
    /// <para>
    /// <b>Read context.</b> The <c>client_credentials</c> grant is anonymous, and the secret package
    /// refuses an unauthenticated context. Unless the caller is already authenticated, the read
    /// runs in a synthesized authenticated service context carrying the ambient tenant, which is
    /// the tenant whose <c>ClientCredentials</c> was just read. A <c>service</c> secret is checked
    /// against the tenant and its status only, so the organization of that context does not
    /// decide the read.
    /// </para>
    /// <para>
    /// <b>Write context.</b> Writes run as the console caller (impersonated into the project). The
    /// secret is filed under the credential's organization, not the caller's.
    /// </para>
    /// </remarks>
    public sealed class ClientCredentialSecretResolver : IClientCredentialSecretResolver
    {
        public const string SecretName = "client-credential-secret";
        public const string NoTenantFailure = "no_tenant";

        /// <summary>Actor recorded on the secret store's audit row for a token-request read.</summary>
        public const string ServiceUserId = "blocks-iam-client-credential";

        /// <summary>Marks the request as this resolver's own, not a real address.</summary>
        private static readonly string ServiceRequestUri = new UriBuilder("internal", "client-credential") { Path = "resolve-secret" }.Uri.ToString();
        private const string DefaultOrganizationId = "default";
        private const int MaxNameInDescription = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ClientCredentialSecretResolver> _logger;

        public ClientCredentialSecretResolver(IServiceScopeFactory scopeFactory, ILogger<ClientCredentialSecretResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // As for identity providers, a whitespace-only reference counts as a reference and fails
        // closed instead of falling back to the plaintext field.
        public Task<string?> GetSecretAsync(ClientCredential credential, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(credential);

            return string.IsNullOrEmpty(credential.ClientSecretReference)
                ? Task.FromResult(credential.ClientSecret)
                : ReadAsync(credential, credential.ClientSecretReference, cancellationToken);
        }

        private async Task<string?> ReadAsync(ClientCredential credential, string reference, CancellationToken cancellationToken)
        {
            var ambient = BlocksContext.GetContext();
            var tenantId = ambient?.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                // Fail closed: the store separates tenants by TenantId alone, so a defaulted
                // tenant would be worse than not resolving.
                throw Unavailable(credential, NoTenantFailure);
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
                throw Unavailable(credential, ex.GetType().Name);
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

        private ClientCredentialSecretUnavailableException Unavailable(ClientCredential credential, string failureClass)
        {
            _logger.LogWarning(
                "The secret of client credential {ClientId} is unavailable: {FailureClass}.",
                credential.ItemId, failureClass);

            return new ClientCredentialSecretUnavailableException(credential.ItemId, failureClass);
        }

        public async Task<string> StoreAsync(ClientCredential credential, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(credential);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            return await secrets.SetAsync(BuildRequest(credential, value), cancellationToken).ConfigureAwait(false);
        }

        public async Task<(string Reference, bool Created)> ReplaceAsync(ClientCredential credential, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(credential);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            var reference = credential.ClientSecretReference;

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
                        "The secret of client credential {ClientId} could not be rotated ({FailureClass}); storing a new secret.",
                        credential.ItemId, ex.GetType().Name);
                }
            }

            var id = await secrets.SetAsync(BuildRequest(credential, value), cancellationToken).ConfigureAwait(false);
            return (id, true);
        }

        public async Task RetireAsync(ClientCredential credential, string? reference, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(credential);

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
                    "Retiring the secret of client credential {ClientId} failed: {FailureClass}. The secret is left orphaned.",
                    credential.ItemId, ex.GetType().Name);
            }
        }

        private static SetSecretRequest BuildRequest(ClientCredential credential, string value) =>
            new()
            {
                Name = SecretName,
                Type = SecretTypes.Service,
                Description = BuildDescription(credential),
                OrganizationId = credential.OrganizationId,
                Value = value,
            };

        private static string BuildDescription(ClientCredential credential)
        {
            var name = credential.Name ?? string.Empty;
            if (name.Length > MaxNameInDescription)
            {
                name = name[..MaxNameInDescription];
            }

            var tenantId = BlocksContext.GetContext()?.TenantId;
            return string.IsNullOrWhiteSpace(tenantId)
                ? $"Client credential {name} ({credential.ItemId}): client secret"
                : $"Client credential {name} ({credential.ItemId}, tenant {tenantId}): client secret";
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
