using System.Text.Json;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// The single validity check every refresh consumer runs. Redis is a cache only — a miss is never an
    /// authorization decision — so MongoDB decides, enforcing both the sliding (idle) window and the
    /// absolute cap. A token superseded by rotation moments ago resolves to its successor instead of
    /// failing, which is what makes a second tab or an HTTP retry an idempotent replay rather than a
    /// suspected theft.
    /// </summary>
    public interface IRefreshSessionResolver
    {
        /// <summary>
        /// Returns the session to continue with, or null when the presented token cannot be used. A
        /// non-null result whose <see cref="RefreshTokenCache.RefreshToken"/> differs from
        /// <paramref name="presentedTokenId"/> is a grace-window replay: the caller must return that
        /// successor without rotating.
        /// </summary>
        Task<RefreshTokenCache?> TryResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration);
    }

    public sealed class RefreshSessionResolver : IRefreshSessionResolver
    {
        internal const string SupersededByRotationReason = "superseded_by_rotation";
        internal const string ReuseDetectedReason = "token_reuse_detected";

        /// <summary>
        /// No natural bound exists in the data. Ten covers any plausible burst inside a five-minute
        /// window while making a corrupted or cyclic chain fail closed rather than loop.
        /// </summary>
        private const int MaxChainHops = 10;

        private readonly ICacheClient _cacheClient;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ILogger<RefreshSessionResolver> _logger;

        public RefreshSessionResolver(
            ICacheClient cacheClient,
            IRefreshTokenRepository refreshTokenRepository,
            ILogger<RefreshSessionResolver> logger)
        {
            _cacheClient = cacheClient;
            _refreshTokenRepository = refreshTokenRepository;
            _logger = logger;
        }

        public async Task<RefreshTokenCache?> TryResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(presentedTokenId))
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var fingerprint = Fingerprint(presentedTokenId);

            var cached = await ReadCacheAsync(presentedTokenId);
            if (cached != null)
            {
                if (now >= cached.ExpiresUtc)
                {
                    LogRejection("sliding_expired", fingerprint, cached.RefreshTokenSessionId, cached.UserId, cached.TenantId, cached.ClientId);
                    return null;
                }

                if (now >= cached.AbsoluteExpiresUtc)
                {
                    LogRejection("absolute_expired", fingerprint, cached.RefreshTokenSessionId, cached.UserId, cached.TenantId, cached.ClientId);
                    return null;
                }

                return cached;
            }

            RefreshTokenModel? persisted;
            try
            {
                persisted = await _refreshTokenRepository.GetByTokenIdAsync(presentedTokenId);
            }
            catch (Exception ex)
            {
                // A transient store outage must not be allowed to destroy a live session, so nothing is
                // revoked or deleted here — the request simply fails.
                _logger.LogError(ex, "Refresh token store read failed for {TokenFingerprint}. reason=store_unavailable", fingerprint);
                return null;
            }

            if (persisted == null)
            {
                LogRejection("not_found", fingerprint, null, null, null, null);
                return null;
            }

            if (persisted.IsRevoked)
            {
                return await ResolveRevokedAsync(persisted, presentedTokenId, configuration, now, fingerprint);
            }

            if (now >= persisted.SlidingExpiry)
            {
                LogRejection("sliding_expired", fingerprint, persisted.EffectiveRefreshTokenSessionId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return null;
            }

            if (now >= persisted.AbsoluteExpiry)
            {
                LogRejection("absolute_expired", fingerprint, persisted.EffectiveRefreshTokenSessionId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return null;
            }

            return await RehydrateAsync(persisted, configuration, now);
        }

        /// <summary>
        /// A revoked token is only recoverable when rotation is what revoked it and the grace window is
        /// still open. Every other revocation — logout, password change, an impersonation transition — is
        /// expected and must not escalate into reuse handling.
        /// </summary>
        private async Task<RefreshTokenCache?> ResolveRevokedAsync(
            RefreshTokenModel persisted,
            string presentedTokenId,
            IdentityConfiguration configuration,
            DateTime now,
            string fingerprint)
        {
            var lineageId = persisted.EffectiveRefreshTokenSessionId;

            if (!string.Equals(persisted.RevokeReason, SupersededByRotationReason, StringComparison.Ordinal))
            {
                LogRejection("revoked", fingerprint, lineageId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return null;
            }

            var graceMinutes = configuration?.TokenRotationGracePeriodMinutes ?? 0;
            var withinGrace = graceMinutes > 0
                              && persisted.RevokedAt.HasValue
                              && now < persisted.RevokedAt.Value.AddMinutes(graceMinutes);

            if (!withinGrace)
            {
                await HandleReuseAsync(persisted, presentedTokenId, fingerprint, lineageId);
                return null;
            }

            // A document written before the grace period shipped carries no successor pointer. Nothing is
            // inferred from any other field — an unguessable successor is treated as replay.
            if (string.IsNullOrWhiteSpace(persisted.SupersededByTokenId))
            {
                await HandleReuseAsync(persisted, presentedTokenId, fingerprint, lineageId);
                return null;
            }

            RefreshTokenModel? successor;
            try
            {
                successor = await FollowSupersededChainAsync(persisted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh token store read failed while following the rotation chain for {TokenFingerprint}. reason=store_unavailable", fingerprint);
                return null;
            }

            if (successor == null || now >= successor.AbsoluteExpiry)
            {
                // A dead end is not evidence of theft: the successor may simply have been logged out.
                // Nothing further is revoked.
                _logger.LogInformation(
                    "Refresh token grace chain reached no usable successor. reason=grace_chain_dead_end tokenFingerprint={TokenFingerprint} refreshTokenSessionId={RefreshTokenSessionId} userId={UserId} tenantId={TenantId} clientId={ClientId}",
                    fingerprint,
                    lineageId,
                    persisted.UserId,
                    persisted.TenantId,
                    persisted.ClientId);
                return null;
            }

            var resolved = await RehydrateAsync(successor, configuration, now);

            _logger.LogInformation(
                "Refresh token replayed onto its rotation successor inside the grace window. reason=grace_successor_returned tokenFingerprint={TokenFingerprint} successorFingerprint={SuccessorFingerprint} refreshTokenSessionId={RefreshTokenSessionId} userId={UserId} tenantId={TenantId} clientId={ClientId}",
                fingerprint,
                Fingerprint(successor.TokenId),
                successor.EffectiveRefreshTokenSessionId,
                successor.UserId,
                successor.TenantId,
                successor.ClientId);

            return resolved;
        }

        /// <summary>
        /// Walks <c>SupersededByTokenId</c> to the first unrevoked document, bounded so a corrupted or
        /// cyclic chain fails closed. Returns null on a missing link, a link revoked for any reason other
        /// than rotation, or exhaustion of the hop budget.
        /// </summary>
        private async Task<RefreshTokenModel?> FollowSupersededChainAsync(RefreshTokenModel start)
        {
            var current = start;

            for (var hop = 0; hop < MaxChainHops; hop++)
            {
                var nextId = current.SupersededByTokenId;
                if (string.IsNullOrWhiteSpace(nextId))
                {
                    return null;
                }

                var next = await _refreshTokenRepository.GetByTokenIdAsync(nextId!);
                if (next == null)
                {
                    return null;
                }

                if (!next.IsRevoked)
                {
                    return next;
                }

                if (!string.Equals(next.RevokeReason, SupersededByRotationReason, StringComparison.Ordinal))
                {
                    return null;
                }

                current = next;
            }

            return null;
        }

        /// <summary>
        /// Replay outside the grace window is the only genuine theft signal available here. The whole
        /// lineage goes, not just the presented token — an attacker holding an old value would otherwise
        /// keep the newest one alive.
        /// </summary>
        private async Task HandleReuseAsync(RefreshTokenModel persisted, string presentedTokenId, string fingerprint, string lineageId)
        {
            var revokedCount = await _refreshTokenRepository.RevokeAllByRefreshTokenSessionIdAsync(lineageId, ReuseDetectedReason);
            await _cacheClient.RemoveKeyAsync(presentedTokenId);

            _logger.LogWarning(
                "Potential refresh token reuse detected; lineage revoked. reason=reuse_detected tokenFingerprint={TokenFingerprint} refreshTokenSessionId={RefreshTokenSessionId} revokedCount={RevokedCount} userId={UserId} tenantId={TenantId} clientId={ClientId}",
                fingerprint,
                lineageId,
                revokedCount,
                persisted.UserId,
                persisted.TenantId,
                persisted.ClientId);
        }

        /// <summary>
        /// Writes the resolved session back into Redis. The rotation that normally follows reads this
        /// entry back out, so it has to be written rather than merely returned.
        /// </summary>
        private async Task<RefreshTokenCache> RehydrateAsync(RefreshTokenModel persisted, IdentityConfiguration configuration, DateTime now)
        {
            var tokenCache = new RefreshTokenCache
            {
                RefreshToken = persisted.TokenId,
                TenantId = persisted.TenantId,
                OrganizationId = persisted.OrganizationId,
                ClientId = persisted.ClientId,
                SessionId = persisted.SessionId ?? string.Empty,
                RefreshTokenSessionId = persisted.EffectiveRefreshTokenSessionId,
                IssuedUtc = persisted.IssuedUtc,
                ExpiresUtc = persisted.SlidingExpiry,
                AbsoluteExpiresUtc = persisted.AbsoluteExpiry,
                UserId = persisted.UserId,
                IpAddresses = persisted.IpAddress,
                Scope = persisted.Scope,
                Impersonated = persisted.Impersonated,
                ImpersonationId = persisted.ImpersonationId
            };

            var (slidingMinutes, _) = RefreshTokenLifetimeResolver.Resolve(configuration, _logger);
            var ttlSeconds = RefreshTokenLifetimeResolver.ResolveCacheTtlSeconds(slidingMinutes, persisted.AbsoluteExpiry, now);

            await _cacheClient.AddStringValueAsync(persisted.TokenId, JsonSerializer.Serialize(tokenCache), ttlSeconds);

            return tokenCache;
        }

        private async Task<RefreshTokenCache?> ReadCacheAsync(string tokenId)
        {
            var raw = await _cacheClient.GetStringValueAsync(tokenId);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<RefreshTokenCache>(raw);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during refresh-token cache read for {TokenFingerprint}", Fingerprint(tokenId));
                return null;
            }
        }

        private void LogRejection(string reason, string fingerprint, string? refreshTokenSessionId, string? userId, string? tenantId, string? clientId)
        {
            _logger.LogInformation(
                "Refresh token rejected. reason={Reason} tokenFingerprint={TokenFingerprint} refreshTokenSessionId={RefreshTokenSessionId} userId={UserId} tenantId={TenantId} clientId={ClientId}",
                reason,
                fingerprint,
                refreshTokenSessionId,
                userId,
                tenantId,
                clientId);
        }

        private static string Fingerprint(string token)
        {
            const int visibleLength = 8;
            return string.IsNullOrEmpty(token) || token.Length <= visibleLength
                ? token
                : string.Concat(token.AsSpan(0, visibleLength), "...");
        }
    }
}
