using System.Text.Json;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Services
{
    /// <summary>
    /// Cache-backed store for the opaque <c>interactionId</c> minted when a user posts a user code.
    /// Bridges the existing OIDC login flow (which knows nothing about device flow) with the
    /// device authorization request state. The cache key is <c>device_interaction:{interactionId}</c>
    /// with TTL = <c>ExpiresAt + 10 minutes</c>. Implicit cleanup on TTL; explicit removal on
    /// Approve / Deny / Expire.
    /// </summary>
    public interface IDeviceInteractionStateStore
    {
        Task SaveAsync(string interactionId, DeviceInteractionContext context, TimeSpan ttl, CancellationToken ct = default);
        Task<DeviceInteractionContext?> GetAsync(string interactionId, CancellationToken ct = default);
        Task RemoveAsync(string interactionId, CancellationToken ct = default);
    }

    public sealed class DeviceInteractionStateStore : IDeviceInteractionStateStore
    {
        private const string KeyPrefix = "device_interaction:";

        private readonly ICacheClient _cacheClient;
        private readonly ILogger<DeviceInteractionStateStore> _logger;

        public DeviceInteractionStateStore(ICacheClient cacheClient, ILogger<DeviceInteractionStateStore> logger)
        {
            _cacheClient = cacheClient;
            _logger = logger;
        }

        private static string Key(string interactionId) => KeyPrefix + interactionId;

        public async Task SaveAsync(string interactionId, DeviceInteractionContext context, TimeSpan ttl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
            {
                throw new ArgumentException("interactionId must not be empty", nameof(interactionId));
            }

            var payload = JsonSerializer.Serialize(context);
            await _cacheClient.CacheDatabase().StringSetAsync(Key(interactionId), payload, ttl);
            _logger.LogDebug("Device interaction {InteractionId} persisted (TTL={TtlSeconds}s)", interactionId, (int)ttl.TotalSeconds);
        }

        public async Task<DeviceInteractionContext?> GetAsync(string interactionId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
            {
                return null;
            }

            var raw = await _cacheClient.CacheDatabase().StringGetAsync(Key(interactionId));
            if (!raw.HasValue || string.IsNullOrEmpty(raw))
            {
                return null;
            }

            try
            {
                var json = raw.ToString();
                return string.IsNullOrEmpty(json)
                    ? null
                    : JsonSerializer.Deserialize<DeviceInteractionContext>(json);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize device interaction context for {InteractionId}", interactionId);
                return null;
            }
        }

        public async Task RemoveAsync(string interactionId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
            {
                return;
            }

            await _cacheClient.CacheDatabase().KeyDeleteAsync(Key(interactionId));
        }
    }
}