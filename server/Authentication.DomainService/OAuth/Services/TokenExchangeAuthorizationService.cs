using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Authentication.DomainService.OAuth.Services
{
    /// <summary>
    /// RFC 8693 token exchange for a Blocks delegation grant.
    /// <para>
    /// A background worker presents the opaque grant id it received in a message header and gets
    /// back a short-lived, ordinary Blocks access token carrying the originating user's context.
    /// The grant may be redeemed as many times as the job needs, and nothing rotates.
    /// </para>
    /// <para>
    /// <b>Identity sourcing is non-negotiable.</b> The tenant comes from
    /// <see cref="BlocksContext"/>, which <c>TenantValidationMiddleware</c> populated from
    /// <c>x-blocks-key</c> before this anonymous endpoint ran. The user and organization come from
    /// the Redis record and from nowhere else: both are caller-influenced anywhere else, including
    /// the message <c>SecurityContext</c>. The record's tenant is cross-checked against
    /// <see cref="BlocksContext"/> and a mismatch is rejected.
    /// </para>
    /// <para>
    /// Validation runs in the fixed order documented in the delegated-access spec, sections 3 and
    /// 7.1: clock window, nonce replay, signature, then -- and only then -- the grant lookup.
    /// A bad signature performs no Redis read at all.
    /// </para>
    /// </summary>
    public sealed class TokenExchangeAuthorizationService : ITokenService
    {
        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private readonly IUserRepository _userRepository;
        private readonly IJwtAccessTokenProvider _jwtAccessTokenProvider;
        private readonly ILogger<TokenExchangeAuthorizationService> _logger;

        public TokenExchangeAuthorizationService(
            ITenants tenants,
            ICacheClient cacheClient,
            IUserRepository userRepository,
            IJwtAccessTokenProvider jwtAccessTokenProvider,
            ILogger<TokenExchangeAuthorizationService> logger)
        {
            _tenants = tenants;
            _cacheClient = cacheClient;
            _userRepository = userRepository;
            _jwtAccessTokenProvider = jwtAccessTokenProvider;
            _logger = logger;
        }

        public async Task<TokenResponse> AuthenticateAsync(
            TokenRequest request,
            IdentityConfiguration authenticationConfiguration,
            User? user = null)
        {
            if (authenticationConfiguration == null)
            {
                return Failure("server_error", "Authentication configuration missing", StatusCodes.Status500InternalServerError);
            }

            var exchange = request?.TokenExchange;
            if (exchange == null)
            {
                return Failure("invalid_request", "Missing token exchange parameters");
            }

            if (!string.Equals(exchange.SubjectTokenType, DelegationConstants.DelegationGrantTokenType, StringComparison.Ordinal))
            {
                return Failure("invalid_request", "Unsupported subject_token_type");
            }

            if (!DelegationPolicy.IsWellFormedGrantId(exchange.SubjectToken))
            {
                // Rejected before any Redis access, so a caller cannot use this endpoint to probe keys.
                return Failure("invalid_grant", "Malformed subject_token");
            }

            // ---- 1. tenant: from BlocksContext only. On this anonymous endpoint it carries the
            //         tenant and nothing else -- there is no authenticated user here.
            var tenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Failure("invalid_client", "Tenant could not be resolved", StatusCodes.Status401Unauthorized);
            }

            // ---- 2. salt
            var tenant = _tenants.GetTenantByID(tenantId!);
            if (tenant == null || string.IsNullOrWhiteSpace(tenant.TenantSalt))
            {
                return Failure("invalid_client", "Tenant could not be resolved", StatusCodes.Status401Unauthorized);
            }

            // ---- 3. clock window
            if (!TryParseTimestamp(exchange.Ts, out var ts) || !IsInsideClockWindow(ts))
            {
                // A hard failure here usually means NTP drift on a worker or an IAM node.
                return Failure("invalid_request", "Timestamp outside the accepted window");
            }

            if (string.IsNullOrWhiteSpace(exchange.Nonce))
            {
                return Failure("invalid_request", "Missing nonce");
            }

            // ---- 4. nonce replay: SETNX. A nonce that already exists is a replay.
            if (!await TryClaimNonceAsync(exchange.SubjectToken!, exchange.Nonce!).ConfigureAwait(false))
            {
                return Failure("invalid_request", "Nonce replay detected");
            }

            // ---- 5. signature, constant-time. No Redis *read* has happened yet.
            var expectedSignature = DelegationSignature.Compute(
                DelegationConstants.BuildSignatureInput(tenantId!, exchange.SubjectToken!, exchange.Nonce!, ts),
                tenant.TenantSalt);

            if (!DelegationSignature.Verify(expectedSignature, exchange.Signature))
            {
                TokenExchangeLog.SignatureMismatch(_logger, tenantId!);
                return Failure("invalid_client", "Signature verification failed", StatusCodes.Status401Unauthorized);
            }

            // ---- 6. grant lookup
            var record = await ReadGrantAsync(exchange.SubjectToken!).ConfigureAwait(false);
            if (record == null)
            {
                return Failure("invalid_grant", "Delegation grant not found or expired");
            }

            // ---- 7. the record's tenant must be the request's tenant
            if (!string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                TokenExchangeLog.TenantMismatch(_logger, tenantId!);
                return Failure("invalid_grant", "Delegation grant does not belong to this tenant");
            }

            if (string.IsNullOrWhiteSpace(record.UserId))
            {
                return Failure("invalid_grant", "Delegation grant carries no user");
            }

            // ---- 8. redemption rate cap
            if (!await IsInsideRateCapAsync(exchange.SubjectToken!).ConfigureAwait(false))
            {
                return Failure("slow_down", "Redemption rate exceeded", StatusCodes.Status429TooManyRequests);
            }

            // ---- 9. current user state from the tenant DB. The grant is a pointer to an identity,
            //         never a snapshot of its authority: everything below is re-read now.
            var delegatedUser = await _userRepository.GetUserByIdAsync(record.UserId).ConfigureAwait(false);
            if (delegatedUser == null || string.IsNullOrWhiteSpace(delegatedUser.ItemId))
            {
                return Failure("invalid_grant", "User no longer exists");
            }

            if (!delegatedUser.Active)
            {
                return Failure("invalid_grant", "User is not active");
            }

            if (!string.Equals(delegatedUser.TokenVersion.ToString(), record.TokenVersion, StringComparison.Ordinal))
            {
                // Every session was revoked since the grant was written.
                return Failure("invalid_grant", "Token version has changed");
            }

            if (!string.Equals(delegatedUser.SecurityStamp ?? string.Empty, record.SecurityStamp, StringComparison.Ordinal))
            {
                // Credentials or security-relevant state changed since the grant was written.
                return Failure("invalid_grant", "Security stamp has changed");
            }

            // ---- 10/11. Mint through GetJwtAccessToken + CreateJwtAccessToken directly.
            //
            // Deliberately NOT through ManageTokenAsync: that path mandatorily issues a refresh
            // token and hard-fails when rotation cannot resolve a predecessor. Delegation must
            // never touch UnifiedTokenSessionService -- that is precisely why nothing rotates here.
            //
            // The organization comes from the record, so roles and permissions resolve for the
            // organization the job was started in. AuthorizationClaimsResolver is called inside
            // GetJwtAccessToken with this OrganizationId.
            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.TokenExchange,
                OrganizationId = record.OrganizationId,
                Request = request?.Request
            };

            var jwtAccessToken = await _jwtAccessTokenProvider
                .GetJwtAccessToken(authenticationConfiguration, tenant, delegatedUser, tokenRequest)
                .ConfigureAwait(false);

            if (jwtAccessToken?.SigningCredentials == null)
            {
                return Failure("server_error", "Unable to resolve the signing certificate", StatusCodes.Status500InternalServerError);
            }

            var accessToken = OAuthJwtAccessTokenManager.CreateJwtAccessToken(jwtAccessToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Failure("server_error", "Unable to mint an access token", StatusCodes.Status500InternalServerError);
            }

            var lifetimeSeconds = Math.Max(
                authenticationConfiguration.AccessTokenValidForNumberMinutes * IdpConstants.SecondsPerMinute,
                IdpConstants.MinAccessTokenLifetimeSeconds);

            TokenExchangeLog.Redeemed(_logger, tenantId!);

            // No refresh token: a delegated token is renewed by redeeming the grant again.
            return new TokenResponse
            {
                AccessToken = accessToken,
                TokenType = "Bearer",
                ExpiresIn = lifetimeSeconds,
                ExpiresUtc = jwtAccessToken.Expires,
                StatusCode = StatusCodes.Status200OK
            };
        }

        private static bool TryParseTimestamp(string? value, out long ts)
            => long.TryParse(value, out ts);

        private static bool IsInsideClockWindow(long ts)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return Math.Abs(now - ts) <= DelegationConstants.ClockWindowSeconds;
        }

        /// <summary>
        /// Claims the nonce with a SETNX. Returns false when it already exists, which is a replay.
        /// </summary>
        private async Task<bool> TryClaimNonceAsync(string delegationId, string nonce)
        {
            try
            {
                return await _cacheClient.CacheDatabase()
                    .StringSetAsync(
                        key: DelegationPolicy.NonceKey(delegationId, nonce),
                        value: "1",
                        expiry: DelegationConstants.NonceTtl,
                        keepTtl: false,
                        when: When.NotExists,
                        flags: CommandFlags.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Failing closed: without a working replay guard the exchange must not proceed.
                TokenExchangeLog.NonceStoreUnavailable(_logger, ex);
                return false;
            }
        }

        private async Task<DelegationGrantRecord?> ReadGrantAsync(string delegationId)
        {
            var json = await _cacheClient.GetStringValueAsync(DelegationPolicy.GrantKey(delegationId)).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<DelegationGrantRecord>(json);
            }
            catch (JsonException ex)
            {
                TokenExchangeLog.GrantUnreadable(_logger, ex);
                return null;
            }
        }

        /// <summary>
        /// A sliding-window counter per grant. The first increment in a window sets the expiry, so
        /// the window advances only when redemptions actually stop.
        /// </summary>
        private async Task<bool> IsInsideRateCapAsync(string delegationId)
        {
            try
            {
                var key = DelegationPolicy.RedemptionKey(delegationId);
                var database = _cacheClient.CacheDatabase();

                var count = await database.StringIncrementAsync(key).ConfigureAwait(false);
                if (count == 1)
                {
                    await database.KeyExpireAsync(key, DelegationPolicy.RedemptionWindow).ConfigureAwait(false);
                }

                return count <= DelegationPolicy.RedemptionsPerWindow;
            }
            catch (Exception ex)
            {
                // A broken counter must not take the exchange down; the other checks still stand.
                TokenExchangeLog.RateCounterUnavailable(_logger, ex);
                return true;
            }
        }

        private static TokenResponse Failure(string error, string description, int statusCode = StatusCodes.Status400BadRequest)
            => new()
            {
                Error = error,
                ErrorDescription = description,
                StatusCode = statusCode
            };
    }

    internal static partial class TokenExchangeLog
    {
        [LoggerMessage(EventId = 8001, Level = LogLevel.Warning, Message = "Delegation token exchange signature mismatch for tenant {TenantId}.")]
        public static partial void SignatureMismatch(ILogger logger, string tenantId);

        [LoggerMessage(EventId = 8002, Level = LogLevel.Warning, Message = "Delegation grant tenant did not match the request tenant {TenantId}.")]
        public static partial void TenantMismatch(ILogger logger, string tenantId);

        [LoggerMessage(EventId = 8003, Level = LogLevel.Warning, Message = "Stored delegation grant could not be deserialized.")]
        public static partial void GrantUnreadable(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 8004, Level = LogLevel.Error, Message = "Nonce replay guard unavailable; rejecting the exchange.")]
        public static partial void NonceStoreUnavailable(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 8005, Level = LogLevel.Warning, Message = "Redemption rate counter unavailable; allowing the exchange.")]
        public static partial void RateCounterUnavailable(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 8006, Level = LogLevel.Information, Message = "Delegation grant redeemed for tenant {TenantId}.")]
        public static partial void Redeemed(ILogger logger, string tenantId);
    }
}
