using Authentication.DomainService.Entities;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// Reads, writes and applies the optional per-OIDC-client token lifetimes.
    /// </summary>
    public interface IOidcClientTokenLifetimeService
    {
        /// <summary>The stored lifetimes of a client, or null when none are stored.</summary>
        Task<OidcClientTokenLifetimes?> GetAsync(string clientId);

        /// <summary>Stores the lifetimes; an entry with no value is deleted instead.</summary>
        Task SaveAsync(string clientId, OidcClientTokenLifetimes lifetimes);

        /// <summary>Deletes the stored lifetimes of a client (no-op when none are stored).</summary>
        Task DeleteAsync(string clientId);

        /// <summary>
        /// Returns the configuration to issue tokens with for <paramref name="clientId"/>. When the client has
        /// no usable stored value, the given <paramref name="projectConfiguration"/> instance is returned
        /// unchanged (same reference), so every existing fallback behaves exactly as before.
        /// </summary>
        Task<IdentityConfiguration?> ApplyAsync(IdentityConfiguration? projectConfiguration, string? clientId);
    }

    public sealed class OidcClientTokenLifetimeService : IOidcClientTokenLifetimeService
    {
        public const string StoreKeyPrefix = "oidc-client-token-lifetimes:";

        private readonly IKeyValueStore _keyValueStore;
        private readonly ILogger<OidcClientTokenLifetimeService> _logger;

        public OidcClientTokenLifetimeService(IKeyValueStore keyValueStore, ILogger<OidcClientTokenLifetimeService> logger)
        {
            _keyValueStore = keyValueStore;
            _logger = logger;
        }

        public static string StoreKey(string clientId) => StoreKeyPrefix + clientId;

        public async Task<OidcClientTokenLifetimes?> GetAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return null;
            }

            var stored = await _keyValueStore.GetAsync<OidcClientTokenLifetimes>(StoreKey(clientId));
            return stored != null && stored.HasAnyValue() ? stored : null;
        }

        public async Task SaveAsync(string clientId, OidcClientTokenLifetimes lifetimes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
            ArgumentNullException.ThrowIfNull(lifetimes);

            if (!lifetimes.HasAnyValue())
            {
                await DeleteAsync(clientId);
                return;
            }

            await _keyValueStore.SetAsync(StoreKey(clientId), new OidcClientTokenLifetimes
            {
                ClientId = clientId,
                AccessTokenValidForNumberMinutes = lifetimes.AccessTokenValidForNumberMinutes,
                RefreshTokenValidForNumberMinutes = lifetimes.RefreshTokenValidForNumberMinutes,
                AbsoluteRefreshTokenValidForNumberMinutes = lifetimes.AbsoluteRefreshTokenValidForNumberMinutes
            });

            _logger.LogInformation(
                "OIDC client token lifetimes saved. clientId={ClientId} accessMinutes={Access} slidingMinutes={Sliding} absoluteMinutes={Absolute}",
                clientId,
                lifetimes.AccessTokenValidForNumberMinutes,
                lifetimes.RefreshTokenValidForNumberMinutes,
                lifetimes.AbsoluteRefreshTokenValidForNumberMinutes);
        }

        public async Task DeleteAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            await _keyValueStore.DeleteAsync(StoreKey(clientId));
        }

        public async Task<IdentityConfiguration?> ApplyAsync(IdentityConfiguration? projectConfiguration, string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return projectConfiguration;
            }

            var stored = await GetAsync(clientId);
            return Apply(projectConfiguration, stored, clientId, _logger);
        }

        /// <summary>
        /// Overlays the usable stored values on a shallow copy of the project configuration. A value outside
        /// the allowed range is ignored (the project value stays) and logged with the client id only.
        /// </summary>
        public static IdentityConfiguration? Apply(
            IdentityConfiguration? projectConfiguration,
            OidcClientTokenLifetimes? lifetimes,
            string? clientId = null,
            ILogger? logger = null)
        {
            if (lifetimes == null)
            {
                return projectConfiguration;
            }

            var access = Usable(lifetimes.AccessTokenValidForNumberMinutes, OidcClientTokenLifetimes.IsValidAccessMinutes, clientId, nameof(OidcClientTokenLifetimes.AccessTokenValidForNumberMinutes), logger);
            var sliding = Usable(lifetimes.RefreshTokenValidForNumberMinutes, OidcClientTokenLifetimes.IsValidRefreshMinutes, clientId, nameof(OidcClientTokenLifetimes.RefreshTokenValidForNumberMinutes), logger);
            var absolute = Usable(lifetimes.AbsoluteRefreshTokenValidForNumberMinutes, OidcClientTokenLifetimes.IsValidRefreshMinutes, clientId, nameof(OidcClientTokenLifetimes.AbsoluteRefreshTokenValidForNumberMinutes), logger);

            if (access == null && sliding == null && absolute == null)
            {
                return projectConfiguration;
            }

            // Every other setting stays exactly the project's.
            return (projectConfiguration ?? new IdentityConfiguration()).WithTokenLifetimes(access, sliding, absolute);
        }

        private static int? Usable(int? value, Func<int, bool> isValid, string? clientId, string field, ILogger? logger)
        {
            if (!value.HasValue)
            {
                return null;
            }

            if (isValid(value.Value))
            {
                return value;
            }

            logger?.LogWarning(
                "Stored OIDC client token lifetime is out of range and was ignored; the project value is used. clientId={ClientId} field={Field}",
                clientId,
                field);
            return null;
        }
    }
}
