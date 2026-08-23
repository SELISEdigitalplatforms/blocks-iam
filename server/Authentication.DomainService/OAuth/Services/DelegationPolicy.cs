using Blocks.Genesis;

namespace Authentication.DomainService.OAuth.Services
{
    /// <summary>
    /// IAM's local policy for delegated access, layered on top of the cross-SDK wire contract.
    /// <para>
    /// Everything that is part of the wire contract — key prefixes, the grant id shape, the
    /// subject-token type, the clock window, the nonce TTL, and the signature scheme — now comes
    /// from <see cref="DelegationConstants"/> and <see cref="DelegationSignature"/> in the
    /// <c>SeliseBlocks.Genesis.OS</c> package. Do not restate any of it here: a second copy is a
    /// second thing to keep in sync with blocks-genesis-py.
    /// </para>
    /// <para>
    /// What remains below is only what the SDK does not publish, because it is IAM's to decide:
    /// the redemption rate limit is enforced server-side and no client needs to agree with it, and
    /// the key builders and the grant-id format check are conveniences over the SDK's prefixes.
    /// </para>
    /// </summary>
    public static class DelegationPolicy
    {
        /// <summary>
        /// Redemption rate window. IAM-local: the exchange endpoint enforces it, and no SDK
        /// needs to know the value, so it is safe to override per deployment.
        /// </summary>
        public static readonly TimeSpan RedemptionWindow = TimeSpan.FromSeconds(60);

        /// <summary>Exchanges permitted per grant inside <see cref="RedemptionWindow"/>.</summary>
        public const int RedemptionsPerWindow = 60;

        public static string GrantKey(string delegationId)
            => $"{DelegationConstants.GrantKeyPrefix}{delegationId}";

        public static string NonceKey(string delegationId, string nonce)
            => $"{DelegationConstants.NonceKeyPrefix}{delegationId}:{nonce}";

        public static string RedemptionKey(string delegationId)
            => $"{DelegationConstants.RedemptionKeyPrefix}{delegationId}";

        /// <summary>
        /// True only for <see cref="DelegationConstants.GrantIdPrefix"/> followed by exactly
        /// <see cref="DelegationConstants.GrantIdRandomBytes"/> * 2 lowercase hex characters.
        /// Rejecting a malformed id before any Redis read keeps a caller from probing keys.
        /// </summary>
        public static bool IsWellFormedGrantId(string? delegationId)
        {
            if (string.IsNullOrWhiteSpace(delegationId)) return false;
            if (!delegationId.StartsWith(DelegationConstants.GrantIdPrefix, StringComparison.Ordinal)) return false;

            var body = delegationId.AsSpan(DelegationConstants.GrantIdPrefix.Length);
            if (body.Length != DelegationConstants.GrantIdRandomBytes * 2) return false;

            foreach (var character in body)
            {
                var isLowerHex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
                if (!isLowerHex) return false;
            }

            return true;
        }
    }
}
