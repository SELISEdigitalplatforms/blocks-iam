using Authentication.DomainService.Dtos;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// The one account-state check every refresh-token rotation runs (<c>/oidc/token</c> refresh grant,
    /// <c>/auth/refresh</c>, switch-organization) after loading the user, so a refresh honours the
    /// account's current state.
    /// <para>
    /// The rule is the user list's own (<see cref="UserAccountStates.Resolve"/>): an account that reads
    /// as <c>Deactivated</c> or <c>Suspended</c> is refused, as is any account carrying a deactivation
    /// stamp. <c>PendingVerification</c> is deliberately NOT refused: an invited or signup-link account
    /// can hold a session before its first activation, and documents written before
    /// <c>Status</c> existed default to it while being fully usable. A bare <c>!Active</c> check
    /// would have logged both out.
    /// </para>
    /// </summary>
    public static class BlockedUserRefreshGuard
    {
        /// <summary>Reason recorded on the refresh tokens revoked when a blocked user tries to refresh.</summary>
        public const string RevokeReason = "user_inactive";

        /// <summary>
        /// Same text as an expired/unknown token, so the response does not tell the caller why the
        /// account is refused.
        /// </summary>
        public const string ErrorDescription = "Refresh token is invalid or expired";

        public static bool IsBlocked(User user)
        {
            if (user.DeactivatedAtUtc.HasValue)
            {
                return true;
            }

            var state = UserAccountStates.Resolve(user.Active, user.Status, user.IsVerified);
            return state is UserAccountState.Deactivated or UserAccountState.Suspended;
        }

        /// <summary>
        /// Revokes and evicts the presented refresh lineage so the client stops retrying a token that
        /// can never succeed. Best effort: the refusal itself already stops the rotation, so a failed
        /// revoke is logged and swallowed.
        /// </summary>
        public static async Task RevokeLineageAsync(
            IRefreshTokenRepository refreshTokenRepository,
            RefreshTokenCache tokenCache,
            ILogger logger)
        {
            var lineageId = string.IsNullOrWhiteSpace(tokenCache.RefreshTokenSessionId)
                ? tokenCache.RefreshToken
                : tokenCache.RefreshTokenSessionId;

            if (string.IsNullOrWhiteSpace(lineageId))
            {
                return;
            }

            try
            {
                await refreshTokenRepository.RevokeLineageAndEvictAsync(lineageId, RevokeReason);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Revoking the refresh lineage of a blocked user failed. userId={UserId} lineageId={LineageId}",
                    tokenCache.UserId,
                    lineageId);
            }
        }
    }
}
