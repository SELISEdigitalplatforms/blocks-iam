using Authentication.DomainService.Entities;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// The single source of truth for the two refresh-token lifetimes. Every writer — Redis TTL, the
    /// MongoDB document and the cookie <c>Expires</c> — derives from one call, so the three can no
    /// longer drift apart. Replaces the silent clamp that used to live inline in
    /// <c>UnifiedTokenSessionService.CreateOrRotateRefreshToken</c>.
    /// </summary>
    public static class RefreshTokenLifetimeResolver
    {
        /// <summary>
        /// Resolves the sliding (idle) and absolute (hard cap) lifetimes in minutes.
        /// Non-positive values fall back to the documented defaults. An absolute cap shorter than the
        /// sliding window is a misconfiguration that cannot be honoured coherently, so it is logged and
        /// raised to the sliding window rather than failing the request.
        /// </summary>
        public static (int SlidingMinutes, int AbsoluteMinutes) Resolve(IdentityConfiguration? configuration, ILogger? logger = null)
        {
            var sliding = configuration is { RefreshTokenValidForNumberMinutes: > 0 }
                ? configuration.RefreshTokenValidForNumberMinutes
                : IdentityConfiguration.DefaultRefreshTokenValidForNumberMinutes;

            var absolute = configuration is { AbsoluteRefreshTokenValidForNumberMinutes: > 0 }
                ? configuration.AbsoluteRefreshTokenValidForNumberMinutes
                : IdentityConfiguration.DefaultAbsoluteRefreshTokenValidForNumberMinutes;

            if (absolute < sliding)
            {
                logger?.LogError(
                    "Invalid identity configuration: AbsoluteRefreshTokenValidForNumberMinutes ({Absolute}) is less than RefreshTokenValidForNumberMinutes ({Sliding}). Using {Sliding} for both.",
                    absolute,
                    sliding,
                    sliding);
                absolute = sliding;
            }

            return (sliding, absolute);
        }

        /// <summary>
        /// The Redis TTL for a token: the sliding window, never reaching past the lineage's cap.
        /// </summary>
        public static int ResolveCacheTtlSeconds(int slidingMinutes, DateTime absoluteExpiry, DateTime now)
        {
            var remainingAbsoluteSeconds = (int)Math.Max(1, (absoluteExpiry - now).TotalSeconds);
            return Math.Min(Math.Max(slidingMinutes, 1) * 60, remainingAbsoluteSeconds);
        }
    }
}
