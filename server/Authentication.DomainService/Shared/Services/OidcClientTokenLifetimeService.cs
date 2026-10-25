using Authentication.DomainService.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// Reads and applies the optional per-OIDC-client token lifetimes. They are stored on the client
    /// document (<see cref="OidcClientRegistration.TokenLifetimes"/>) and written with it.
    /// </summary>
    public interface IOidcClientTokenLifetimeService
    {
        /// <summary>The client's own lifetimes, or null when the client has none (= project values).</summary>
        Task<OidcClientTokenLifetimes?> GetAsync(string clientId);

        /// <summary>
        /// Returns the configuration to issue tokens with for <paramref name="clientId"/>. When the client has
        /// no usable stored value, the given <paramref name="projectConfiguration"/> instance is returned
        /// unchanged (same reference), so every existing fallback behaves exactly as before.
        /// </summary>
        Task<IdentityConfiguration?> ApplyAsync(IdentityConfiguration? projectConfiguration, string? clientId);
    }

    public sealed class OidcClientTokenLifetimeService : IOidcClientTokenLifetimeService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ILogger<OidcClientTokenLifetimeService> _logger;

        public OidcClientTokenLifetimeService(IAuthenticationRepository authenticationRepository, ILogger<OidcClientTokenLifetimeService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _logger = logger;
        }

        public async Task<OidcClientTokenLifetimes?> GetAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return null;
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            return OwnValues(client?.TokenLifetimes);
        }

        /// <summary>The lifetimes to store or report: null when no field is set, so "all empty" = project values.</summary>
        public static OidcClientTokenLifetimes? OwnValues(OidcClientTokenLifetimes? lifetimes) =>
            lifetimes != null && lifetimes.HasAnyValue()
                ? new OidcClientTokenLifetimes
                {
                    AccessTokenValidForNumberMinutes = lifetimes.AccessTokenValidForNumberMinutes,
                    RefreshTokenValidForNumberMinutes = lifetimes.RefreshTokenValidForNumberMinutes,
                    AbsoluteRefreshTokenValidForNumberMinutes = lifetimes.AbsoluteRefreshTokenValidForNumberMinutes
                }
                : null;

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
