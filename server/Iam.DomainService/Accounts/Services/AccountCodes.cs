using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// The one-time codes mailed for a password reset and for an account activation.
    /// The code key still maps code -> user id. Next to it, one key per user and purpose holds
    /// the latest code issued for that purpose, with the same lifetime. A code is accepted only
    /// when it is that latest code: so it works for its own purpose only, a newer code retires
    /// every older one, and any other cache key that happens to hold a user id is refused.
    /// </summary>
    public static class AccountCodes
    {
        public const string Reset = "reset";
        public const string Activation = "activation";

        public static string LatestKey(string purpose, string userId) => $"iam:{purpose}-latest:{userId}";

        public static async Task IssueAsync(ICacheClient cacheClient, string purpose, string code, string userId, long lifetimeSeconds)
        {
            ArgumentNullException.ThrowIfNull(cacheClient);
            await cacheClient.AddStringValueAsync(code, userId, lifetimeSeconds);
            await cacheClient.AddStringValueAsync(LatestKey(purpose, userId), code, lifetimeSeconds);
        }

        /// <summary>The user id the code belongs to, or null when it is not the user's latest live code for <paramref name="purpose"/>.</summary>
        public static async Task<string?> ResolveAsync(ICacheClient cacheClient, string purpose, string? code)
        {
            ArgumentNullException.ThrowIfNull(cacheClient);
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            var userId = await cacheClient.GetStringValueAsync(code);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var latest = await cacheClient.GetStringValueAsync(LatestKey(purpose, userId));
            return string.Equals(latest, code, StringComparison.Ordinal) ? userId : null;
        }

        /// <summary>Spends the code: it and every older code of the same purpose stop working.</summary>
        public static async Task RetireAsync(ICacheClient cacheClient, string purpose, string code, string userId)
        {
            ArgumentNullException.ThrowIfNull(cacheClient);
            await cacheClient.RemoveKeyAsync(code);
            await cacheClient.RemoveKeyAsync(LatestKey(purpose, userId));
        }

        /// <summary>
        /// Blocked by an administrator (disabled, suspended or deactivated). Such an account gets no
        /// recovery mail and cannot activate itself with a code; only an administrator lifts it.
        /// Same rule the signup-link redemption applies.
        /// </summary>
        public static bool IsBlocked(User user) =>
            user is null
            || user.Status is UserLifecycleStatus.Disabled or UserLifecycleStatus.Suspended
            || user.DeactivatedAtUtc.HasValue;
    }
}
