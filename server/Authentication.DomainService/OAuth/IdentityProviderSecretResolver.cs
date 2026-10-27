using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    /// <summary>The two identity-provider credentials kept in Blocks Secrets.</summary>
    public enum IdentityProviderSecretKind
    {
        ClientSecret,
        PrivateKey,
    }

    /// <summary>
    /// Reads and writes the client secret and Apple private key of an identity provider.
    /// </summary>
    /// <remarks>
    /// Reference rule: a non-empty <c>ClientSecretReference</c> / <c>PrivateKeyReference</c> is
    /// authoritative and its value lives in Blocks Secrets; an empty one means the legacy field
    /// still holds the value. When a reference is set the legacy field is never used, not even
    /// as a fallback when the read fails.
    /// </remarks>
    public interface IIdentityProviderSecretResolver
    {
        /// <summary>
        /// The client secret to send to the provider's token endpoint.
        /// </summary>
        /// <exception cref="IdentityProviderSecretUnavailableException">The referenced secret cannot be read.</exception>
        Task<string?> GetClientSecretAsync(IdentityProvider provider, CancellationToken cancellationToken = default);

        /// <summary>
        /// The Apple private key (PEM) used to sign the client assertion.
        /// </summary>
        /// <exception cref="IdentityProviderSecretUnavailableException">The referenced secret cannot be read.</exception>
        Task<string?> GetPrivateKeyAsync(IdentityProvider provider, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores the supplied non-empty values as new <c>service</c> secrets in one call:
        /// <c>SetAsync</c> for one value, all-or-nothing <c>SetManyAsync</c> for both. Returns the
        /// new references (null for a value that was not supplied). Secret-store exceptions propagate.
        /// </summary>
        Task<(string? ClientSecretReference, string? PrivateKeyReference)> StoreAsync(
            IdentityProvider provider, string? clientSecret, string? privateKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces one credential: rotates the existing reference in place, or stores a new
        /// secret when there is no reference or the old one is missing or deleted
        /// (<see cref="SecretNotFoundException"/> / <see cref="SecretStateException"/>).
        /// Other secret-store exceptions propagate.
        /// </summary>
        Task<(string Reference, bool Created)> ReplaceAsync(
            IdentityProvider provider, IdentityProviderSecretKind kind, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-deletes a secret. Never throws apart from cancellation: not-found and state
        /// failures are ignored, anything else is logged as a warning.
        /// </summary>
        Task RetireAsync(IdentityProvider provider, IdentityProviderSecretKind kind, string? reference, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A referenced identity-provider credential could not be read. Carries the provider name,
    /// the credential kind and the failure class only: never the value, the reference id or the
    /// underlying exception, whose message can name the reference id.
    /// </summary>
    public sealed class IdentityProviderSecretUnavailableException : Exception
    {
        public IdentityProviderSecretUnavailableException(string? providerName, IdentityProviderSecretKind kind, string failureClass)
            : base($"The {kind} of identity provider '{providerName}' is unavailable ({failureClass}).")
        {
            ProviderName = providerName;
            Kind = kind;
            FailureClass = failureClass;
        }

        public string? ProviderName { get; }

        public IdentityProviderSecretKind Kind { get; }

        /// <summary><see cref="IdentityProviderSecretResolver.NoTenantFailure"/> or the type name of the exception that failed the read.</summary>
        public string FailureClass { get; }
    }

    /// <summary>
    /// Blocks Secrets access for identity-provider credentials.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lifetime.</b> Registered as a singleton (the domain services that use it are singletons),
    /// while <see cref="ISecretService"/> is scoped. A DI scope is created per call and the
    /// service resolved from it, so no tenant's instance is captured. Nothing is cached.
    /// </para>
    /// <para>
    /// <b>Read context.</b> Sign-in callbacks are anonymous, and the secret package refuses an
    /// unauthenticated context. Unless the caller is already authenticated (then the real caller
    /// is the audit actor), the read runs in a synthesized authenticated service context carrying
    /// the ambient tenant, which is the tenant whose <c>IdentityProviders</c> was just read. The
    /// pattern is the one <c>CaptchaSecretResolver</c> uses, copied here with its own actor.
    /// </para>
    /// <para>
    /// <b>Write context.</b> Writes run as the console caller (impersonated into the project).
    /// </para>
    /// </remarks>
    public sealed class IdentityProviderSecretResolver : IIdentityProviderSecretResolver
    {
        public const string ClientSecretName = "identity-provider-client-secret";
        public const string PrivateKeyName = "identity-provider-private-key";
        public const string NoTenantFailure = "no_tenant";

        /// <summary>Actor recorded on the secret store's audit row for a sign-in read.</summary>
        public const string ServiceUserId = "blocks-iam-identity-provider";

        /// <summary>Marks the request as this resolver's own, not a real address.</summary>
        private static readonly string ServiceRequestUri = new UriBuilder("internal", "identity-provider") { Path = "resolve-secret" }.Uri.ToString();
        private const string DefaultOrganizationId = "default";
        private const int MaxProviderNameInDescription = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<IdentityProviderSecretResolver> _logger;

        public IdentityProviderSecretResolver(IServiceScopeFactory scopeFactory, ILogger<IdentityProviderSecretResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task<string?> GetClientSecretAsync(IdentityProvider provider, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return string.IsNullOrEmpty(provider.ClientSecretReference)
                ? Task.FromResult<string?>(provider.ClientSecret)
                : ReadAsync(provider, IdentityProviderSecretKind.ClientSecret, provider.ClientSecretReference, cancellationToken);
        }

        public Task<string?> GetPrivateKeyAsync(IdentityProvider provider, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return string.IsNullOrEmpty(provider.PrivateKeyReference)
                ? Task.FromResult(provider.PrivateKey)
                : ReadAsync(provider, IdentityProviderSecretKind.PrivateKey, provider.PrivateKeyReference, cancellationToken);
        }

        private async Task<string?> ReadAsync(
            IdentityProvider provider, IdentityProviderSecretKind kind, string reference, CancellationToken cancellationToken)
        {
            var ambient = BlocksContext.GetContext();
            var tenantId = ambient?.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                // Fail closed: the store separates tenants by TenantId alone, so a defaulted
                // tenant would be worse than not resolving.
                throw Unavailable(provider, kind, NoTenantFailure);
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
                throw Unavailable(provider, kind, ex.GetType().Name);
            }
        }

        private async Task<string> ReadAsServiceAsync(BlocksContext ambient, string tenantId, string reference, CancellationToken cancellationToken)
        {
            // The ambient context is unauthenticated here, so GetContext() returned the AsyncLocal
            // value: that is what is written back.
            var restore = ambient;

            try
            {
                var organizationId = string.IsNullOrWhiteSpace(ambient.OrganizationId) ? DefaultOrganizationId : ambient.OrganizationId;

                // changeContext: true so the secret package sees this context even where the
                // request carries an (unauthenticated) HTTP identity.
                BlocksContext.SetContext(BuildServiceContext(tenantId, organizationId), true);

                return await GetValueInScopeAsync(reference, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                BlocksContext.SetContext(restore, restore is not null);
            }
        }

        private async Task<string> GetValueInScopeAsync(string reference, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            return await secrets.GetValueAsync(reference, cancellationToken).ConfigureAwait(false);
        }

        private IdentityProviderSecretUnavailableException Unavailable(IdentityProvider provider, IdentityProviderSecretKind kind, string failureClass)
        {
            _logger.LogWarning(
                "The {SecretKind} of identity provider {Provider} is unavailable: {FailureClass}.",
                kind, provider.Provider, failureClass);

            return new IdentityProviderSecretUnavailableException(provider.Provider, kind, failureClass);
        }

        public async Task<(string? ClientSecretReference, string? PrivateKeyReference)> StoreAsync(
            IdentityProvider provider, string? clientSecret, string? privateKey, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(provider);

            var requests = new List<SetSecretRequest>(2);
            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                requests.Add(BuildRequest(provider, IdentityProviderSecretKind.ClientSecret, clientSecret));
            }

            if (!string.IsNullOrWhiteSpace(privateKey))
            {
                requests.Add(BuildRequest(provider, IdentityProviderSecretKind.PrivateKey, privateKey));
            }

            if (requests.Count == 0)
            {
                return (null, null);
            }

            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            if (requests.Count == 1)
            {
                var id = await secrets.SetAsync(requests[0], cancellationToken).ConfigureAwait(false);
                return requests[0].Name == ClientSecretName ? (id, null) : (null, id);
            }

            // Both or neither: SetManyAsync compensates internally when one write fails.
            var ids = await secrets.SetManyAsync(requests, cancellationToken).ConfigureAwait(false);
            return (ids[ClientSecretName], ids[PrivateKeyName]);
        }

        public async Task<(string Reference, bool Created)> ReplaceAsync(
            IdentityProvider provider, IdentityProviderSecretKind kind, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(provider);

            var reference = kind == IdentityProviderSecretKind.ClientSecret ? provider.ClientSecretReference : provider.PrivateKeyReference;

            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

            if (!string.IsNullOrEmpty(reference))
            {
                try
                {
                    // A Locked secret rotates successfully and stays Locked (Secrets 4.0.3), so
                    // sign-in keeps failing until an admin unlocks it on the Secrets page. That is
                    // accepted: locking is a deliberate admin action, and checking the status first
                    // would cost a call outside the update budget.
                    await secrets.RotateAsync(reference, new RotateSecretRequest { Value = value }, cancellationToken).ConfigureAwait(false);
                    return (reference, false);
                }
                catch (Exception ex) when (ex is SecretNotFoundException or SecretStateException)
                {
                    // The old secret is unusable and left as it is; a new one takes its place.
                    _logger.LogInformation(
                        "The {SecretKind} of identity provider {Provider} could not be rotated ({FailureClass}); storing a new secret.",
                        kind, provider.Provider, ex.GetType().Name);
                }
            }

            var id = await secrets.SetAsync(BuildRequest(provider, kind, value), cancellationToken).ConfigureAwait(false);
            return (id, true);
        }

        public async Task RetireAsync(IdentityProvider provider, IdentityProviderSecretKind kind, string? reference, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(provider);

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
                    "Retiring the {SecretKind} of identity provider {Provider} failed: {FailureClass}. The secret is left orphaned.",
                    kind, provider.Provider, ex.GetType().Name);
            }
        }

        private static SetSecretRequest BuildRequest(IdentityProvider provider, IdentityProviderSecretKind kind, string value)
        {
            var isClientSecret = kind == IdentityProviderSecretKind.ClientSecret;

            return new SetSecretRequest
            {
                Name = isClientSecret ? ClientSecretName : PrivateKeyName,
                Type = SecretTypes.Service,
                Description = BuildDescription(provider, isClientSecret ? "client secret" : "private key"),
                Value = value,
            };
        }

        // No provider id: the repository assigns ItemId on insert, after the secret is stored.
        private static string BuildDescription(IdentityProvider provider, string what)
        {
            var name = provider.Provider ?? string.Empty;
            if (name.Length > MaxProviderNameInDescription)
            {
                name = name[..MaxProviderNameInDescription];
            }

            var tenantId = BlocksContext.GetContext()?.TenantId;
            return string.IsNullOrWhiteSpace(tenantId)
                ? $"Identity provider {name}: {what}"
                : $"Identity provider {name} (tenant {tenantId}): {what}";
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
