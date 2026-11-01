using System.Text.Json;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Iam;

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
        /// <para>
        /// A token replaced by an impersonation start, stop or switch does not resolve here: its
        /// successor is in a different tenant (or organization) than the one the caller holds.
        /// </para>
        /// </summary>
        Task<RefreshTokenCache?> TryResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration);

        /// <summary>
        /// The same check, also saying when nothing came back because an impersonation start, stop or
        /// switch replaced the token (<see cref="RefreshSessionResolution.SessionChanged"/>).
        /// <para>
        /// A refresh passes <paramref name="followImpersonationTransitions"/> false: handing it the newest
        /// token would put the caller in a tenant it never asked for, behind an ordinary 200. Impersonation
        /// start and stop pass true — they move the session on purpose, and a retry whose first answer was
        /// lost must reach the session that answer created.
        /// </para>
        /// </summary>
        Task<RefreshSessionResolution> ResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration, bool followImpersonationTransitions);
    }

    /// <summary>The outcome of <see cref="IRefreshSessionResolver.ResolveRefreshSessionAsync"/>.</summary>
    /// <param name="Session">The session to continue with, or null when the presented token cannot be used.</param>
    /// <param name="SessionChanged">
    /// True when the token cannot be used only because an impersonation start, stop or switch replaced it
    /// inside the grace window. Not a sign of theft, and the lineage stays live: the client's cookie may
    /// already hold the newer token.
    /// </param>
    public readonly record struct RefreshSessionResolution(RefreshTokenCache? Session, bool SessionChanged)
    {
        public static RefreshSessionResolution None => default;

        public static RefreshSessionResolution Changed => new(null, true);
    }

    public sealed class RefreshSessionResolver : IRefreshSessionResolver
    {
        internal const string SupersededByRotationReason = "superseded_by_rotation";
        internal const string SupersededByImpersonationReason = "superseded_by_impersonation";
        internal const string ReuseDetectedReason = "token_reuse_detected";

        /// <summary>
        /// Lets a refresh follow an impersonation start, stop or switch again, as it did before
        /// <see cref="SupersededByImpersonationReason"/> existed. Off by default; an operator switch for
        /// clients that cannot yet handle a <c>session_changed</c> refusal.
        /// </summary>
        internal const string RefreshFollowsImpersonationSetting = "RefreshTokens:RefreshFollowsImpersonationTransitions";

        /// <summary>
        /// No natural bound exists in the data. Ten covers any plausible burst inside a five-minute
        /// window while making a corrupted or cyclic chain fail closed rather than loop.
        /// </summary>
        private const int MaxChainHops = 10;

        private readonly ICacheClient _cacheClient;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ILogger<RefreshSessionResolver> _logger;
        private readonly IOidcClientTokenLifetimeService? _clientTokenLifetimes;
        private readonly bool _refreshFollowsImpersonation;

        public RefreshSessionResolver(
            ICacheClient cacheClient,
            IRefreshTokenRepository refreshTokenRepository,
            ILogger<RefreshSessionResolver> logger,
            IOidcClientTokenLifetimeService? clientTokenLifetimes = null,
            IConfiguration? appConfiguration = null)
        {
            _clientTokenLifetimes = clientTokenLifetimes;
            _cacheClient = cacheClient;
            _refreshTokenRepository = refreshTokenRepository;
            _logger = logger;
            _refreshFollowsImpersonation = appConfiguration?.GetValue<bool>(RefreshFollowsImpersonationSetting) ?? false;
        }

        public async Task<RefreshTokenCache?> TryResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration)
            => (await ResolveRefreshSessionAsync(presentedTokenId, configuration, followImpersonationTransitions: false)).Session;

        public async Task<RefreshSessionResolution> ResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration, bool followImpersonationTransitions)
        {
            if (string.IsNullOrWhiteSpace(presentedTokenId))
            {
                return RefreshSessionResolution.None;
            }

            var now = DateTime.UtcNow;
            var fingerprint = Fingerprint(presentedTokenId);

            var cached = await ReadCacheAsync(presentedTokenId);
            if (cached != null)
            {
                if (now >= cached.ExpiresUtc)
                {
                    LogRejection("sliding_expired", fingerprint, cached.RefreshTokenSessionId, cached.UserId, cached.TenantId, cached.ClientId);
                    return RefreshSessionResolution.None;
                }

                if (now >= cached.AbsoluteExpiresUtc)
                {
                    LogRejection("absolute_expired", fingerprint, cached.RefreshTokenSessionId, cached.UserId, cached.TenantId, cached.ClientId);
                    return RefreshSessionResolution.None;
                }

                return new RefreshSessionResolution(cached, false);
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
                return RefreshSessionResolution.None;
            }

            if (persisted == null)
            {
                LogRejection("not_found", fingerprint, null, null, null, null);
                return RefreshSessionResolution.None;
            }

            if (persisted.IsRevoked)
            {
                var followTransitions = followImpersonationTransitions || _refreshFollowsImpersonation;
                return await ResolveRevokedAsync(persisted, presentedTokenId, configuration, now, fingerprint, followTransitions);
            }

            if (now >= persisted.SlidingExpiry)
            {
                LogRejection("sliding_expired", fingerprint, persisted.EffectiveRefreshTokenSessionId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return RefreshSessionResolution.None;
            }

            if (now >= persisted.AbsoluteExpiry)
            {
                LogRejection("absolute_expired", fingerprint, persisted.EffectiveRefreshTokenSessionId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return RefreshSessionResolution.None;
            }

            return new RefreshSessionResolution(await RehydrateAsync(persisted, configuration, now), false);
        }

        /// <summary>
        /// A revoked token is only recoverable when a rotation, or (for callers that follow them) an
        /// impersonation start, stop or switch, is what revoked it, and the grace window is still open.
        /// Every other revocation — logout, password change — is expected and must not escalate into
        /// reuse handling.
        /// <para>
        /// A caller that does not follow impersonation transitions gets <see cref="RefreshSessionResolution.Changed"/>
        /// for a token one replaced inside the grace window: a refresh that lost the race with an
        /// impersonation change, or whose client never received that change's answer. Nothing is revoked
        /// — the newer token may already be in the client's cookie. Outside the window it is reuse, as
        /// before.
        /// </para>
        /// </summary>
        private async Task<RefreshSessionResolution> ResolveRevokedAsync(
            RefreshTokenModel persisted,
            string presentedTokenId,
            IdentityConfiguration configuration,
            DateTime now,
            string fingerprint,
            bool followImpersonationTransitions)
        {
            var lineageId = persisted.EffectiveRefreshTokenSessionId;
            var replacedByImpersonation = string.Equals(persisted.RevokeReason, SupersededByImpersonationReason, StringComparison.Ordinal);

            if (!replacedByImpersonation && !string.Equals(persisted.RevokeReason, SupersededByRotationReason, StringComparison.Ordinal))
            {
                LogRejection("revoked", fingerprint, lineageId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return RefreshSessionResolution.None;
            }

            var graceMinutes = configuration?.TokenRotationGracePeriodMinutes ?? 0;
            var withinGrace = graceMinutes > 0
                              && persisted.RevokedAt.HasValue
                              && now < persisted.RevokedAt.Value.AddMinutes(graceMinutes);

            if (!withinGrace)
            {
                await HandleReuseAsync(persisted, presentedTokenId, fingerprint, lineageId);
                return RefreshSessionResolution.None;
            }

            if (replacedByImpersonation && !followImpersonationTransitions)
            {
                LogRejection("session_changed", fingerprint, lineageId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return RefreshSessionResolution.Changed;
            }

            // A document written before the grace period shipped carries no successor pointer. Nothing is
            // inferred from any other field — an unguessable successor is treated as replay.
            if (string.IsNullOrWhiteSpace(persisted.SupersededByTokenId))
            {
                await HandleReuseAsync(persisted, presentedTokenId, fingerprint, lineageId);
                return RefreshSessionResolution.None;
            }

            RefreshTokenModel? successor;
            bool sessionChanged;
            try
            {
                (successor, sessionChanged) = await FollowSupersededChainAsync(persisted, followImpersonationTransitions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh token store read failed while following the rotation chain for {TokenFingerprint}. reason=store_unavailable", fingerprint);
                return RefreshSessionResolution.None;
            }

            if (sessionChanged)
            {
                LogRejection("session_changed", fingerprint, lineageId, persisted.UserId, persisted.TenantId, persisted.ClientId);
                return RefreshSessionResolution.Changed;
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
                return RefreshSessionResolution.None;
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

            return new RefreshSessionResolution(resolved, false);
        }

        /// <summary>
        /// Walks <c>SupersededByTokenId</c> to the first unrevoked document, bounded so a corrupted or
        /// cyclic chain fails closed. Returns no successor on a missing link, a link revoked for any reason
        /// other than rotation, or exhaustion of the hop budget. A link an impersonation change replaced is
        /// followed only when <paramref name="followImpersonationTransitions"/> is set; otherwise the walk
        /// stops there and reports the session as changed.
        /// </summary>
        private async Task<(RefreshTokenModel? Successor, bool SessionChanged)> FollowSupersededChainAsync(RefreshTokenModel start, bool followImpersonationTransitions)
        {
            var current = start;

            for (var hop = 0; hop < MaxChainHops; hop++)
            {
                var nextId = current.SupersededByTokenId;
                if (string.IsNullOrWhiteSpace(nextId))
                {
                    return (null, false);
                }

                var next = await _refreshTokenRepository.GetByTokenIdAsync(nextId!);
                if (next == null)
                {
                    return (null, false);
                }

                if (!next.IsRevoked)
                {
                    return (next, false);
                }

                if (string.Equals(next.RevokeReason, SupersededByImpersonationReason, StringComparison.Ordinal))
                {
                    if (!followImpersonationTransitions)
                    {
                        return (null, true);
                    }
                }
                else if (!string.Equals(next.RevokeReason, SupersededByRotationReason, StringComparison.Ordinal))
                {
                    return (null, false);
                }

                current = next;
            }

            return (null, false);
        }

        /// <summary>
        /// Replay outside the grace window is the only genuine theft signal available here. The whole
        /// lineage goes, not just the presented token — an attacker holding an old value would otherwise
        /// keep the newest one alive. Every lineage key leaves the cache too, because a cache hit is not
        /// checked against the store.
        /// </summary>
        private async Task HandleReuseAsync(RefreshTokenModel persisted, string presentedTokenId, string fingerprint, string lineageId)
        {
            var revokedCount = await _refreshTokenRepository.RevokeLineageAndEvictAsync(lineageId, ReuseDetectedReason);
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
                ImpersonationId = persisted.ImpersonationId,
                IsLinkAuthentication = persisted.IsLinkAuthentication,
                RestrictedRoles = persisted.RestrictedRoles?.ToList() ?? [],
                RestrictedPermissions = persisted.RestrictedPermissions?.ToList() ?? [],
                Amr = persisted.Amr?.ToList() ?? [],
                Audience = persisted.Audience
            };

            // The token's own client decides the idle window when it has one set; otherwise the project value.
            var effectiveConfiguration = _clientTokenLifetimes == null
                ? configuration
                : await _clientTokenLifetimes.ApplyAsync(configuration, persisted.ClientId);
            var (slidingMinutes, _) = RefreshTokenLifetimeResolver.Resolve(effectiveConfiguration, _logger);
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
